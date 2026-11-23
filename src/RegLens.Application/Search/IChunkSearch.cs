using RegLens.Domain.Search;

namespace RegLens.Application.Search;

/// <summary>Searches stored chunks (spec 0002, R2.4–R2.7).</summary>
public interface IChunkSearch
{
    /// <summary>Dense and keyword candidate lists fused with Reciprocal Rank Fusion, best first, at most <see cref="SearchQuery.Limit"/>.</summary>
    Task<IReadOnlyList<SearchHit>> HybridAsync(SearchQuery query, CancellationToken cancellationToken);

    /// <summary>Every chunk a citation names, with its ranks from the same query's candidate lists where it appears there.</summary>
    Task<IReadOnlyList<SearchHit>> FindByCitationsAsync(
        IReadOnlyList<Citation> citations, SearchQuery query, CancellationToken cancellationToken);
}
