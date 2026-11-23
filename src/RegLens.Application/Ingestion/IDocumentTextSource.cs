using RegLens.Domain.Corpus;

namespace RegLens.Application.Ingestion;

/// <summary>Extracts page text from a corpus document, limited to its ingest pages (spec 0001, R1.11).</summary>
public interface IDocumentTextSource
{
    IReadOnlyList<PageText> ReadPages(SourceDocument document);
}
