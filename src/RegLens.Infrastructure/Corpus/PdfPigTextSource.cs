using Microsoft.Extensions.Options;
using RegLens.Application.Ingestion;
using RegLens.Domain.Corpus;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace RegLens.Infrastructure.Corpus;

/// <summary>
/// Extracts page text with PdfPig's <see cref="ContentOrderTextExtractor"/>, which PdfPig's README recommends
/// for RAG over <c>page.Text</c> (spec 0001, R1.11). Only the document's ingest pages are read.
/// </summary>
public sealed class PdfPigTextSource(IOptions<CorpusOptions> options) : IDocumentTextSource
{
    public IReadOnlyList<PageText> ReadPages(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var pdf = PdfDocument.Open(Path.Combine(options.Value.ResolveRoot(), document.FileName));
        return pdf.GetPages()
            .Where(page => document.IngestPages.Contains(page.Number))
            .Select(page => new PageText(page.Number, ContentOrderTextExtractor.GetText(page)))
            .ToList();
    }
}
