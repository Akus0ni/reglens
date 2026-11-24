using RegLens.Domain.Corpus;

namespace RegLens.Application.Ingestion;

/// <summary>Whether the store already has this document, and in which version.</summary>
public enum StoredDocumentState
{
    Missing,
    Outdated,
    Current,
}

/// <summary>Where chunks and their vectors are kept (spec 0001, R1.21).</summary>
public interface IChunkStore
{
    /// <summary>
    /// Compares the stored copy of this document with its SHA-256 and the pipeline that built it: either differing
    /// makes it <see cref="StoredDocumentState.Outdated"/> (spec 0005, P6).
    /// </summary>
    Task<StoredDocumentState> GetStateAsync(SourceDocument document, string pipeline, CancellationToken cancellationToken);

    /// <summary>Replaces the document, its pipeline and all its chunks in one transaction.</summary>
    Task ReplaceDocumentAsync(
        SourceDocument document, string pipeline, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken);

    /// <summary>Recomputes the corpus-wide word statistics keyword scoring reads (spec 0004, R4.4).</summary>
    Task RefreshSearchStatisticsAsync(CancellationToken cancellationToken);
}
