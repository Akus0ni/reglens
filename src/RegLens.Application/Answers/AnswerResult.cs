namespace RegLens.Application.Answers;

/// <summary>The response of <c>POST /ask</c> (spec 0003, R3.15).</summary>
public sealed record AnswerResult(
    string Question,
    string Answer,
    bool Abstained,
    string? AbstainReason,
    string MissingInfo,
    IReadOnlyList<AnswerCitation> Citations,
    int DroppedCitations,
    string? Degraded,
    bool Cached,
    string Model,
    AnswerUsage? Usage)
{
    public const string LowSimilarity = "low-similarity";
    public const string ModelAbstained = "model-abstained";
    public const string Unverified = "unverified";
    public const string ModelRefused = "model-refused";
}

/// <summary>A verified citation with where it is in the corpus.</summary>
public sealed record AnswerCitation(
    long ChunkId,
    string DocumentId,
    string DocumentTitle,
    string ParaRef,
    int PageStart,
    int PageEnd,
    string Quote);

public sealed record AnswerUsage(long InputTokens, long OutputTokens);

/// <summary>What went wrong when no answer could be produced (spec 0003, P11, P12).</summary>
public enum AnswerFailure
{
    /// <summary>The model could not be reached, or kept failing after the SDK's retries. HTTP 503.</summary>
    ModelUnavailable,

    /// <summary>The model hit the output token limit, or returned output that does not fit the schema. HTTP 502.</summary>
    InvalidModelOutput,
}

public sealed class AnswerGenerationException : Exception
{
    public AnswerGenerationException()
    {
    }

    public AnswerGenerationException(string message)
        : base(message)
    {
    }

    public AnswerGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AnswerGenerationException(AnswerFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public AnswerFailure Failure { get; }
}
