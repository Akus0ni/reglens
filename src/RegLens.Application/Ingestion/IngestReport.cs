namespace RegLens.Application.Ingestion;

public enum IngestStatus
{
    Skipped,
    Ingested,
    Replaced,
}

/// <summary>Smallest, median and largest token estimate of a document's chunks.</summary>
public sealed record TokenStats(int Min, int Median, int Max)
{
    public static TokenStats None { get; } = new(0, 0, 0);

    public static TokenStats Of(IReadOnlyCollection<int> estimates)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        if (estimates.Count == 0)
        {
            return None;
        }

        var sorted = estimates.Order().ToList();
        return new TokenStats(sorted[0], sorted[sorted.Count / 2], sorted[^1]);
    }
}

/// <summary>What happened to one document (spec 0001, R1.24).</summary>
public sealed record DocumentIngestResult(
    string DocumentId,
    IngestStatus Status,
    int PagesRead,
    int ChunksWritten,
    TokenStats Tokens,
    TimeSpan Elapsed,
    ContextStats Context);

/// <summary>What happened to the whole run.</summary>
public sealed record IngestReport(IReadOnlyList<DocumentIngestResult> Documents, TimeSpan Elapsed)
{
    public int TotalChunksWritten => Documents.Sum(d => d.ChunksWritten);

    public double TotalContextCostUsd => Documents.Sum(d => d.Context.CostUsd);
}

/// <summary>Options for one run (spec 0001, R1.23).</summary>
public sealed record IngestRequest(bool Force = false, string? OnlyDocumentId = null);
