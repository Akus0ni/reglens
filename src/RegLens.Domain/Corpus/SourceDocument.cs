namespace RegLens.Domain.Corpus;

/// <summary>One official document from the corpus manifest, with the provenance needed to cite it.</summary>
public sealed record SourceDocument(
    string Id,
    string FileName,
    string Title,
    string Reference,
    string Version,
    string SourceUrl,
    string Sha256,
    long Bytes,
    int Pages,
    PageRange IngestPages,
    DocumentStructure Structure);
