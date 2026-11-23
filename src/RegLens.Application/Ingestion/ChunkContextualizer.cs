using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Application.Ingestion;

/// <summary>What contextual retrieval did for one document (spec 0005, R5.13).</summary>
public sealed record ContextStats(int Generated, int FromCache, TokenUsage Usage, double CostUsd)
{
    public static ContextStats None { get; } = new(0, 0, TokenUsage.None, 0);
}

/// <summary>Each chunk with its context (null when contextual retrieval is off), in chunk order.</summary>
public sealed record DocumentContexts(IReadOnlyList<ContextualChunk> Chunks, ContextStats Stats);

/// <summary>
/// Gives every chunk of a document a context (spec 0005, R5.6–R5.9): reuse what <see cref="IChunkContextCache"/> has,
/// generate the rest. The first missing chunk goes alone so its response writes the prompt cache; the others follow
/// <see cref="ContextOptions.Concurrency"/> at a time and read it (P4). Each context is saved when it arrives, so a
/// failure later in the document costs nothing on the next run (D54).
/// </summary>
public sealed partial class ChunkContextualizer(
    IChunkContextGenerator generator,
    IChunkContextCache cache,
    IOptions<ContextOptions> options,
    ILogger<ChunkContextualizer> logger)
{
    /// <summary>Longer than this is not a "short succinct context" (R5.9); about 20× the 50–100 tokens expected.</summary>
    public const int MaxContextCharacters = 2000;

    private readonly ContextOptions _options = options.Value;

    public async Task<DocumentContexts> ContextualizeAsync(
        SourceDocument document, IReadOnlyList<PageText> pages, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);

        var documentText = ContextPrompt.DocumentText(pages);
        var documentHash = Sha256(documentText);
        var hashes = chunks.Select(chunk => InputHash(documentHash, chunk)).ToArray();
        var cached = await cache.GetAsync(hashes.Distinct().ToList(), cancellationToken).ConfigureAwait(false);

        var contexts = new string?[chunks.Count];
        var missing = new List<int>();
        for (var index = 0; index < chunks.Count; index++)
        {
            if (cached.TryGetValue(hashes[index], out var context))
            {
                contexts[index] = context;
            }
            else
            {
                missing.Add(index);
            }
        }

        var usage = TokenUsage.None;
        var gate = new Lock();
        async ValueTask GenerateAsync(int index, CancellationToken token)
        {
            var generated = await GenerateOneAsync(document, documentText, chunks[index], token).ConfigureAwait(false);
            await cache.SaveAsync(
                new CachedContext(hashes[index], _options.Model, ContextPrompt.Version, generated.Text, generated.Usage),
                token).ConfigureAwait(false);
            contexts[index] = generated.Text;
            lock (gate)
            {
                usage += generated.Usage;
            }
        }

        if (missing.Count > 0)
        {
            LogGenerating(document.Id, missing.Count, chunks.Count);
            await GenerateAsync(missing[0], cancellationToken).ConfigureAwait(false);
            await Parallel.ForEachAsync(
                missing.Skip(1),
                new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency, CancellationToken = cancellationToken },
                GenerateAsync).ConfigureAwait(false);
        }

        var stats = new ContextStats(missing.Count, chunks.Count - missing.Count, usage, ModelPricing.Cost(_options.Model, usage));
        return new DocumentContexts(chunks.Select((chunk, index) => new ContextualChunk(chunk, contexts[index])).ToList(), stats);
    }

    /// <summary>
    /// SHA-256 over the prompt version, model, document text (as its own SHA-256), heading, reference and chunk text (P5).
    /// </summary>
    public string InputHash(string documentHash, Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return Sha256(string.Join('\0', ContextPrompt.Version, _options.Model, documentHash, chunk.Heading, chunk.ParaRef, chunk.Content));
    }

    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private async Task<GeneratedContext> GenerateOneAsync(
        SourceDocument document, string documentText, Chunk chunk, CancellationToken cancellationToken)
    {
        GeneratedContext generated;
        try
        {
            generated = await generator.GenerateAsync(new ContextRequest(documentText, chunk), cancellationToken).ConfigureAwait(false);
        }
        // The SDK's own request timeout surfaces as a cancellation nobody asked for: that is a failure too.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ContextGenerationException(
                $"Context for {document.Id} '{chunk.ParaRef}' failed: {exception.Message}. The document was not replaced.", exception);
        }

        var text = generated.Text.Trim();
        if (text.Length == 0 || text.Length > MaxContextCharacters)
        {
            throw new ContextGenerationException(
                $"Context for {document.Id} '{chunk.ParaRef}' has {text.Length} characters (allowed 1–{MaxContextCharacters}). " +
                "The document was not replaced.");
        }

        return generated with { Text = text };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{DocumentId}: generating {Missing} of {Chunks} chunk contexts")]
    private partial void LogGenerating(string documentId, int missing, int chunks);
}
