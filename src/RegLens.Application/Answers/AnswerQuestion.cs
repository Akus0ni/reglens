using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RegLens.Application.Search;
using RegLens.Domain.Answers;

namespace RegLens.Application.Answers;

/// <summary>
/// The answer use case (spec 0003, R3.5–R3.10): search, abstain early when nothing is close, otherwise ask the model
/// for a structured answer, keep only the citations that verify, and cache the result.
/// </summary>
public sealed partial class AnswerQuestion(
    SearchRegulations search,
    IPassageReader passages,
    IChatClient chat,
    HybridCache cache,
    IIndexVersion indexVersion,
    IOptions<AnswerOptions> options,
    ILogger<AnswerQuestion> logger)
{
    public const string NoCloseMatch = "No passage in the corpus is close enough to the question.";
    public const string NoVerifiedCitation = "The answer could not be verified against the retrieved passages.";
    public const string Refused = "The model declined to answer.";

    private readonly AnswerOptions _options = options.Value;

    public async Task<AnswerResult> RunAsync(string question, int topK, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var started = Stopwatch.GetTimestamp();
        var key = CacheKey(question, topK, await indexVersion.GetAsync(cancellationToken).ConfigureAwait(false));
        var entry = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromHours(_options.CacheHours),
            LocalCacheExpiration = TimeSpan.FromHours(_options.LocalCacheHours),
        };

        // A concurrent caller that joined someone else's factory run did not call the model either, so it counts
        // as a cache hit (R3.10).
        var produced = false;
        AnswerResult result;
        try
        {
            result = await cache.GetOrCreateAsync(
                key,
                async ct =>
                {
                    produced = true;
                    var fresh = await ProduceAsync(question, topK, ct).ConfigureAwait(false);
                    // HybridCache never stores a failed factory, which is how P9's cases stay out of the cache.
                    return IsCacheable(fresh) ? fresh : throw new UncacheableAnswerException(fresh);
                },
                entry,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (UncacheableAnswerException uncacheable)
        {
            produced = true;
            result = uncacheable.Result;
        }

        if (!produced)
        {
            result = result with { Cached = true, Usage = null };
        }

        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogAnswered(result.Cached, result.Abstained, result.AbstainReason ?? "none", result.Citations.Count,
            result.DroppedCitations, result.Usage?.InputTokens ?? 0, result.Usage?.OutputTokens ?? 0, elapsedMs);
        return result;
    }

    /// <summary>
    /// <c>answer:{promptVersion}:{model}:{effort}:{indexVersion}:{topK}:{sha256(normalised question)}</c> (P8). Hashing keeps the
    /// key short and the question text out of Redis key names.
    /// </summary>
    public string CacheKey(string question, int topK, string version)
    {
        ArgumentNullException.ThrowIfNull(question);
        var normalised = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
        return string.Create(CultureInfo.InvariantCulture,
            $"answer:{AnswerPrompt.Version}:{_options.Model}:{_options.Effort}:{version}:{topK}:{hash}");
    }

    private static bool IsCacheable(AnswerResult result) =>
        result.Degraded is null && result.AbstainReason != AnswerResult.ModelRefused;

    private async Task<AnswerResult> ProduceAsync(string question, int topK, CancellationToken cancellationToken)
    {
        var found = await search.RunAsync(question, topK, cancellationToken).ConfigureAwait(false);
        var hits = found.Hits;

        // The floor needs a vector, and a cited paragraph always goes to the model (R3.6, P2, D38).
        var bestSimilarity = hits.Max(hit => hit.Similarity) ?? 0;
        if (hits.Count == 0
            || (found.Degraded is null && !hits.Any(hit => hit.Pinned) && bestSimilarity < _options.SimilarityFloor))
        {
            LogFloorAbstention(bestSimilarity, _options.SimilarityFloor);
            return Abstention(question, AnswerResult.LowSimilarity, NoCloseMatch, found.Degraded, usage: null);
        }

        var stored = await passages.GetAsync([.. hits.Select(hit => hit.ChunkId)], cancellationToken).ConfigureAwait(false);
        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, AnswerPrompt.System),
                new ChatMessage(ChatRole.User, AnswerPrompt.UserMessage(question, stored)),
            ],
            ChatOptions(),
            cancellationToken).ConfigureAwait(false);
        var usage = response.Usage is { } u ? new AnswerUsage(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0) : null;

        // A refusal or a cut-off response may not match the schema (structured outputs docs), so check first (R3.9).
        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            return Abstention(question, AnswerResult.ModelRefused, Refused, found.Degraded, usage);
        }

        if (response.FinishReason == ChatFinishReason.Length)
        {
            throw new AnswerGenerationException(AnswerFailure.InvalidModelOutput,
                $"The model reached the {_options.MaxOutputTokens}-token output limit before finishing.");
        }

        var draft = AnswerPrompt.Parse(response.Text)
            ?? throw new AnswerGenerationException(AnswerFailure.InvalidModelOutput, "The model's output does not match the answer schema.");

        if (draft.Abstained)
        {
            return Abstention(question, AnswerResult.ModelAbstained, draft.MissingInfo, found.Degraded, usage);
        }

        var check = CitationVerifier.Verify(
            [.. draft.Citations.Select(c => new CitedQuote(c.ChunkId, c.Quote))],
            stored.ToDictionary(p => p.ChunkId, p => p.Content));
        foreach (var dropped in check.Dropped)
        {
            LogCitationDropped(dropped.Citation.ChunkId, dropped.Reason);
        }

        if (check.Verified.Count == 0)
        {
            return Abstention(question, AnswerResult.Unverified, NoVerifiedCitation, found.Degraded, usage) with
            {
                DroppedCitations = check.Dropped.Count,
            };
        }

        var byId = stored.ToDictionary(p => p.ChunkId);
        var citations = check.Verified
            .Select(c =>
            {
                var p = byId[c.ChunkId];
                return new AnswerCitation(p.ChunkId, p.DocumentId, p.DocumentTitle, p.ParaRef, p.PageStart, p.PageEnd, c.Quote);
            })
            .ToList();
        return new AnswerResult(question, draft.Answer, Abstained: false, AbstainReason: null, draft.MissingInfo,
            citations, check.Dropped.Count, found.Degraded, Cached: false, _options.Model, usage);
    }

    private ChatOptions ChatOptions() => new()
    {
        ResponseFormat = AnswerPrompt.ResponseFormat,
        MaxOutputTokens = _options.MaxOutputTokens,
        Reasoning = new ReasoningOptions { Effort = Enum.Parse<ReasoningEffort>(_options.Effort) },
    };

    private AnswerResult Abstention(string question, string reason, string missingInfo, string? degraded, AnswerUsage? usage) =>
        new(question, Answer: "", Abstained: true, reason, missingInfo, [], DroppedCitations: 0, degraded,
            Cached: false, _options.Model, usage);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Answered: cached {Cached}, abstained {Abstained} ({Reason}), citations {Kept} kept / {Dropped} dropped, tokens {InputTokens} in / {OutputTokens} out, {ElapsedMs} ms")]
    private partial void LogAnswered(bool cached, bool abstained, string reason, int kept, int dropped,
        long inputTokens, long outputTokens, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Best similarity {Similarity:F3} is below the floor {Floor:F3}; not calling the model")]
    private partial void LogFloorAbstention(double similarity, double floor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped a citation of chunk {ChunkId}: {Reason}")]
    private partial void LogCitationDropped(long chunkId, DropReason reason);

    /// <summary>Carries a result out of the cache factory so HybridCache does not store it (P9).</summary>
    private sealed class UncacheableAnswerException(AnswerResult result) : Exception
    {
        public AnswerResult Result { get; } = result;
    }
}
