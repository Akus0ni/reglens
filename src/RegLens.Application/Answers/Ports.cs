namespace RegLens.Application.Answers;

/// <summary>
/// A chunk's full text and where it comes from (spec 0003, R3.2), with its heading path (spec 0005, P11). The generated
/// context is not part of it: it serves retrieval only and never reaches the answer model (D52).
/// </summary>
public sealed record StoredPassage(
    long ChunkId,
    string DocumentId,
    string DocumentTitle,
    string ParaRef,
    string Heading,
    int PageStart,
    int PageEnd,
    string Content);

/// <summary>Reads full passages by chunk id (spec 0003, R3.2).</summary>
public interface IPassageReader
{
    /// <summary>The passages for <paramref name="chunkIds"/>, in that order; unknown ids are left out.</summary>
    Task<IReadOnlyList<StoredPassage>> GetAsync(IReadOnlyList<long> chunkIds, CancellationToken cancellationToken);
}

/// <summary>A value that changes whenever any document is (re)ingested, used in answer cache keys (spec 0003, P8).</summary>
public interface IIndexVersion
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}
