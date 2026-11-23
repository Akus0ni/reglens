using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Application.Ingestion;

/// <summary>
/// The ingestion use case (spec 0001). Verifies the whole corpus before writing anything, then per document:
/// skip if unchanged, otherwise extract, chunk, embed and replace atomically. Hosts (console, later the API) only call this.
/// </summary>
public sealed partial class IngestCorpus(
    ICorpusCatalog catalog,
    IDocumentTextSource textSource,
    DocumentChunker chunker,
    ChunkEmbedder embedder,
    IChunkStore store,
    ILogger<IngestCorpus> logger)
{
    public async Task<IngestReport> RunAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();

        var documents = Select(await catalog.LoadAsync(cancellationToken).ConfigureAwait(false), request.OnlyDocumentId);
        var results = new List<DocumentIngestResult>(documents.Count);
        foreach (var document in documents)
        {
            var result = await IngestAsync(document, request.Force, cancellationToken).ConfigureAwait(false);
            LogDocument(result.DocumentId, result.Status, result.PagesRead, result.ChunksWritten,
                result.Tokens.Min, result.Tokens.Median, result.Tokens.Max, result.Elapsed.TotalSeconds);
            results.Add(result);
        }

        // BM25 reads corpus-wide statistics; they change only when a document was written (R4.4).
        if (results.Any(result => result.Status != IngestStatus.Skipped))
        {
            await store.RefreshSearchStatisticsAsync(cancellationToken).ConfigureAwait(false);
        }

        var report = new IngestReport(results, Stopwatch.GetElapsedTime(started));
        LogTotal(report.Documents.Count, report.TotalChunksWritten, report.Elapsed.TotalSeconds);
        return report;
    }

    private static IReadOnlyList<SourceDocument> Select(IReadOnlyList<SourceDocument> documents, string? onlyDocumentId)
    {
        if (onlyDocumentId is null)
        {
            return documents;
        }

        var match = documents.Where(d => d.Id == onlyDocumentId).ToList();
        return match.Count == 1
            ? match
            : throw new ArgumentException(
                $"No corpus document '{onlyDocumentId}'. Known ids: {string.Join(", ", documents.Select(d => d.Id))}.",
                nameof(onlyDocumentId));
    }

    private async Task<DocumentIngestResult> IngestAsync(SourceDocument document, bool force, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var state = await store.GetStateAsync(document, cancellationToken).ConfigureAwait(false);
        if (state == StoredDocumentState.Current && !force)
        {
            return new DocumentIngestResult(document.Id, IngestStatus.Skipped, 0, 0, TokenStats.None, Stopwatch.GetElapsedTime(started));
        }

        var pages = textSource.ReadPages(document);
        var chunks = chunker.Chunk(document, pages);
        var embedded = await embedder.EmbedAsync(chunks, cancellationToken).ConfigureAwait(false);
        await store.ReplaceDocumentAsync(document, embedded, cancellationToken).ConfigureAwait(false);

        return new DocumentIngestResult(
            document.Id,
            state == StoredDocumentState.Missing ? IngestStatus.Ingested : IngestStatus.Replaced,
            pages.Count,
            chunks.Count,
            TokenStats.Of(chunks.Select(c => c.TokenEstimate).ToList()),
            Stopwatch.GetElapsedTime(started));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{DocumentId}: {Status}, {Pages} pages, {Chunks} chunks, tokens min {Min} / median {Median} / max {Max}, {Seconds:F1}s")]
    private partial void LogDocument(string documentId, IngestStatus status, int pages, int chunks, int min, int median, int max, double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingested {Documents} documents, {Chunks} chunks written, {Seconds:F1}s total")]
    private partial void LogTotal(int documents, int chunks, double seconds);
}
