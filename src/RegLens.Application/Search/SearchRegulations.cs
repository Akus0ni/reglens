using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RegLens.Domain.Search;

namespace RegLens.Application.Search;

/// <summary>
/// The search use case (spec 0002, R2.8–R2.10): embed the question, run hybrid search, look up any cited paragraph,
/// and put cited matches first. If the embedding fails, fall back to keyword-only search and say so.
/// </summary>
public sealed partial class SearchRegulations(
    IChunkSearch search,
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<SearchOptions> options,
    ILogger<SearchRegulations> logger)
{
    private readonly SearchOptions _options = options.Value;

    public async Task<SearchResult> RunAsync(string question, int topK, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(topK, _options.MaxTopK);

        var embedding = await EmbedAsync(question, cancellationToken).ConfigureAwait(false);
        var query = new SearchQuery(question, embedding.Vector, _options.CandidatesPerSide, topK);
        var citations = CitationQuery.Parse(question);

        var ranked = await search.HybridAsync(query, cancellationToken).ConfigureAwait(false);
        var cited = citations.Count == 0
            ? []
            : await search.FindByCitationsAsync(citations, query, cancellationToken).ConfigureAwait(false);

        return new SearchResult(question, embedding.Degraded, Merge(cited, ranked, topK));
    }

    /// <summary>Cited matches first (best fused score first), then the fused ranking, without duplicates (spec 0002, P3).</summary>
    public static IReadOnlyList<SearchHit> Merge(IReadOnlyList<SearchHit> cited, IReadOnlyList<SearchHit> ranked, int topK)
    {
        ArgumentNullException.ThrowIfNull(cited);
        ArgumentNullException.ThrowIfNull(ranked);

        var pinned = cited
            .OrderByDescending(hit => hit.FusedScore)
            .ThenBy(hit => hit.DocumentId, StringComparer.Ordinal)
            .ThenBy(hit => hit.ChunkId)
            .Select(hit => hit with { Pinned = true });
        var seen = new HashSet<long>();
        return pinned.Concat(ranked)
            .Where(hit => seen.Add(hit.ChunkId))
            .Take(topK)
            .ToList();
    }

    private async Task<QueryEmbedding> EmbedAsync(string question, CancellationToken cancellationToken)
    {
        try
        {
            var vector = await generator
                .GenerateVectorAsync(_options.QueryPrefix + question, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new QueryEmbedding(vector, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Ollama down, a timeout or a circuit breaker: keep answering with keyword search (spec 0002, P5).
            LogVectorSearchUnavailable(exception);
            return new QueryEmbedding(ReadOnlyMemory<float>.Empty, SearchResult.VectorSearchUnavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query embedding failed; answering with keyword search only")]
    private partial void LogVectorSearchUnavailable(Exception exception);

    private sealed record QueryEmbedding(ReadOnlyMemory<float> Vector, string? Degraded);
}
