namespace RegLens.Application.Search;

/// <summary>One search result with the ranks that explain its position (spec 0002, R2.5, D29).</summary>
public sealed record SearchHit(
    long ChunkId,
    string DocumentId,
    string DocumentTitle,
    string ParaRef,
    string Heading,
    int PageStart,
    int PageEnd,
    string Excerpt,
    int? DenseRank,
    int? KeywordRank,
    double FusedScore,
    bool Pinned = false);
