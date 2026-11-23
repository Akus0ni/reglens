namespace RegLens.Application.Search;

/// <summary>
/// One search result with the ranks that explain its position (spec 0002, R2.5, D29). <see cref="Similarity"/> is
/// 1 − cosine distance when the hit is in the dense list, else null (spec 0003, R3.1).
/// </summary>
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
    double? Similarity = null,
    bool Pinned = false);
