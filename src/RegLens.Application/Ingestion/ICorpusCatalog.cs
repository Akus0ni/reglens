using RegLens.Domain.Corpus;

namespace RegLens.Application.Ingestion;

/// <summary>The corpus documents, verified against their recorded size and SHA-256 (spec 0001, R1.10).</summary>
public interface ICorpusCatalog
{
    /// <summary>Loads every document. Throws <see cref="CorpusIntegrityException"/> before returning if any file is wrong.</summary>
    Task<IReadOnlyList<SourceDocument>> LoadAsync(CancellationToken cancellationToken);
}
