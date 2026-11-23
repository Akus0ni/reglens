using RegLens.Domain.Chunking;

namespace RegLens.Application.Ingestion;

/// <summary>One chunk to situate within its whole document (spec 0005, R5.6).</summary>
public sealed record ContextRequest(string DocumentText, Chunk Chunk);

/// <summary>The model's context for one chunk and what the call cost in tokens.</summary>
public sealed record GeneratedContext(string Text, TokenUsage Usage);

/// <summary>Writes the short context that situates a chunk in its document (spec 0005, R5.6, P1–P3).</summary>
public interface IChunkContextGenerator
{
    Task<GeneratedContext> GenerateAsync(ContextRequest request, CancellationToken cancellationToken);
}

/// <summary>A generated context, keyed by the hash of everything that went into it (spec 0005, P5).</summary>
public sealed record CachedContext(string InputHash, string Model, string PromptVersion, string Context, TokenUsage Usage);

/// <summary>Generated contexts kept outside the document transaction, so a re-run pays only for what is missing (D54).</summary>
public interface IChunkContextCache
{
    /// <summary>The cached contexts among <paramref name="inputHashes"/>, by hash.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAsync(IReadOnlyCollection<string> inputHashes, CancellationToken cancellationToken);

    /// <summary>Saves one context as soon as it arrives; an existing row for the hash is kept.</summary>
    Task SaveAsync(CachedContext context, CancellationToken cancellationToken);
}

/// <summary>A chunk's context could not be generated, so its document is not replaced (spec 0005, R5.8, R5.9).</summary>
public sealed class ContextGenerationException : Exception
{
    public ContextGenerationException()
    {
    }

    public ContextGenerationException(string message)
        : base(message)
    {
    }

    public ContextGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
