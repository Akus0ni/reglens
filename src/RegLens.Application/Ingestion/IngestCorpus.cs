using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RegLens.Application.Telemetry;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Application.Ingestion;

/// <summary>
/// The ingestion use case (spec 0001). Verifies the whole corpus before writing anything, then per document:
/// skip if unchanged (file and pipeline), otherwise extract, chunk, contextualize (spec 0005), embed and replace
/// atomically. Hosts only call this.
/// </summary>
public sealed partial class IngestCorpus(
    ICorpusCatalog catalog,
    IDocumentTextSource textSource,
    DocumentChunker chunker,
    ChunkContextualizer contextualizer,
    ChunkEmbedder embedder,
    IChunkStore store,
    IOptions<ContextOptions> contextOptions,
    ILogger<IngestCorpus> logger)
{
    private readonly ContextOptions _context = contextOptions.Value;

    /// <summary>
    /// What builds a document's chunks besides its file (spec 0005, P6): "chunker 2; context claude-haiku-5-5 v1" or
    /// "chunker 2; no context". A stored document built by another pipeline is re-ingested.
    /// </summary>
    public static string Pipeline(ContextOptions context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Enabled
            ? $"{DocumentChunker.Version}; context {context.Model} {ContextPrompt.Version}"
            : $"{DocumentChunker.Version}; no context";
    }

    public async Task<IngestReport> RunAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = RegLensActivities.Source.StartActivity("ingest");
        var started = Stopwatch.GetTimestamp();

        var documents = Select(await catalog.LoadAsync(cancellationToken).ConfigureAwait(false), request.OnlyDocumentId);
        var results = new List<DocumentIngestResult>(documents.Count);
        foreach (var document in documents)
        {
            var result = await IngestAsync(document, request.Force, cancellationToken).ConfigureAwait(false);
            LogDocument(result.DocumentId, result.Status, result.PagesRead, result.ChunksWritten,
                result.Tokens.Min, result.Tokens.Median, result.Tokens.Max, result.Elapsed.TotalSeconds);
            if (result.Context.Generated + result.Context.FromCache > 0)
            {
                var usage = result.Context.Usage;
                LogContext(result.DocumentId, result.Context.Generated, result.Context.FromCache, usage.InputTokens,
                    usage.CacheWriteTokens, usage.CacheReadTokens, usage.OutputTokens, result.Context.CostUsd);
            }

            results.Add(result);
        }

        // BM25 reads corpus-wide statistics; they change only when a document was written (R4.4).
        if (results.Any(result => result.Status != IngestStatus.Skipped))
        {
            await store.RefreshSearchStatisticsAsync(cancellationToken).ConfigureAwait(false);
        }

        var report = new IngestReport(results, Stopwatch.GetElapsedTime(started));
        LogTotal(report.Documents.Count, report.TotalChunksWritten, report.TotalContextCostUsd, report.Elapsed.TotalSeconds);
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
        using var activity = RegLensActivities.Source.StartActivity("ingest " + document.Id);
        var started = Stopwatch.GetTimestamp();
        var pipeline = Pipeline(_context);
        activity?.SetTag("reglens.pipeline", pipeline);
        var state = await store.GetStateAsync(document, pipeline, cancellationToken).ConfigureAwait(false);
        if (state == StoredDocumentState.Current && !force)
        {
            return new DocumentIngestResult(
                document.Id, IngestStatus.Skipped, 0, 0, TokenStats.None, Stopwatch.GetElapsedTime(started), ContextStats.None);
        }

        var pages = textSource.ReadPages(document);
        var chunks = chunker.Chunk(document, pages);

        // A context failure throws before anything is written, so the stored document stays as it was (D54).
        var contexts = _context.Enabled
            ? await contextualizer.ContextualizeAsync(document, pages, chunks, cancellationToken).ConfigureAwait(false)
            : new DocumentContexts(chunks.Select(chunk => new ContextualChunk(chunk, null)).ToList(), ContextStats.None);
        var embedded = await embedder.EmbedAsync(contexts.Chunks, cancellationToken).ConfigureAwait(false);
        await store.ReplaceDocumentAsync(document, pipeline, embedded, cancellationToken).ConfigureAwait(false);

        return new DocumentIngestResult(
            document.Id,
            state == StoredDocumentState.Missing ? IngestStatus.Ingested : IngestStatus.Replaced,
            pages.Count,
            chunks.Count,
            TokenStats.Of(chunks.Select(c => c.TokenEstimate).ToList()),
            Stopwatch.GetElapsedTime(started),
            contexts.Stats);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{DocumentId}: {Status}, {Pages} pages, {Chunks} chunks, tokens min {Min} / median {Median} / max {Max}, {Seconds:F1}s")]
    private partial void LogDocument(string documentId, IngestStatus status, int pages, int chunks, int min, int median, int max, double seconds);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{DocumentId}: contexts {Generated} generated / {FromCache} from cache; tokens in {Input}, cache write {CacheWrite}, cache read {CacheRead}, out {Output}; ${Cost:F4}")]
    private partial void LogContext(
        string documentId, int generated, int fromCache, long input, long cacheWrite, long cacheRead, long output, double cost);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ingested {Documents} documents, {Chunks} chunks written, context cost ${Cost:F4}, {Seconds:F1}s total")]
    private partial void LogTotal(int documents, int chunks, double cost, double seconds);
}
