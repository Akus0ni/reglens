namespace RegLens.Application.Search;

/// <summary>The answer to one search. <see cref="Degraded"/> says what was unavailable, or is null when nothing was.</summary>
public sealed record SearchResult(string Query, string? Degraded, IReadOnlyList<SearchHit> Hits)
{
    public const string VectorSearchUnavailable = "vector search unavailable";
}
