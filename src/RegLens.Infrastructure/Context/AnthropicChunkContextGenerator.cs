using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RegLens.Application;
using RegLens.Application.Ingestion;

namespace RegLens.Infrastructure.Context;

/// <summary>
/// Chunk contexts from Claude through <see cref="IChatClient"/> (spec 0005, R5.6, P1–P3). The document is the first
/// user content block with a 5-minute cache breakpoint, so every chunk after a document's first reads it from the
/// prompt cache; the chunk is the second block. <see cref="AIContentCacheExtensions.WithCacheControl{T}(T, Ttl?)"/> only
/// works with the client from <c>AsIChatClient</c> (Anthropic 13.12.0 XML docs).
/// </summary>
public sealed class AnthropicChunkContextGenerator(IChatClient chatClient, IOptions<ContextOptions> options) : IChunkContextGenerator
{
    /// <summary>How the Anthropic chat client reports cache writes in <see cref="UsageDetails.AdditionalCounts"/>.</summary>
    public const string CacheWriteCountKey = "CacheCreationInputTokens";

    private readonly ContextOptions _options = options.Value;

    public async Task<GeneratedContext> GenerateAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent(ContextPrompt.DocumentBlock(request.DocumentText)).WithCacheControl((Ttl?)null),
            new TextContent(ContextPrompt.ChunkBlock(request.Chunk)),
        ]);

        // No temperature, top_p, top_k or prefill: Haiku 5.5 rejects or discourages them (P3).
        var response = await chatClient.GetResponseAsync(
            [message],
            new ChatOptions
            {
                ModelId = _options.Model,
                MaxOutputTokens = _options.MaxOutputTokens,
                Reasoning = new ReasoningOptions { Effort = Enum.Parse<ReasoningEffort>(_options.Effort) },
            },
            cancellationToken).ConfigureAwait(false);

        // ChatResponse.Text joins text blocks only; thinking blocks are left out (P3).
        return new GeneratedContext(response.Text, Usage(response.Usage));
    }

    /// <summary>
    /// The client reports all input in <see cref="UsageDetails.InputTokenCount"/>, cache reads in
    /// <see cref="UsageDetails.CachedInputTokenCount"/> and cache writes in <see cref="CacheWriteCountKey"/>
    /// (measured 2026-11-24 against the raw message usage), so uncached input is what remains.
    /// </summary>
    public static TokenUsage Usage(UsageDetails? usage)
    {
        if (usage is null)
        {
            return TokenUsage.None;
        }

        var read = usage.CachedInputTokenCount ?? 0;
        var write = usage.AdditionalCounts?.TryGetValue(CacheWriteCountKey, out var written) == true ? written : 0;
        var input = Math.Max(0, (usage.InputTokenCount ?? 0) - read - write);
        return new TokenUsage(input, write, read, usage.OutputTokenCount ?? 0);
    }
}
