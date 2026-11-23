using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RegLens.Domain.Chunking;

namespace RegLens.Application.Ingestion;

/// <summary>
/// Embeds chunks through a bounded channel with a fixed number of consumers (spec 0001, R1.18–R1.19).
/// The channel is the backpressure: when Ollama is slow, the producer waits instead of queueing everything in memory.
/// A chunk's context, when it has one, is embedded with it (spec 0005, R5.11, P7).
/// </summary>
public sealed class ChunkEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<EmbeddingOptions> options)
{
    private readonly EmbeddingOptions _options = options.Value;

    /// <summary>Embeds chunks without context.</summary>
    public Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return EmbedAsync(chunks.Select(chunk => new ContextualChunk(chunk, null)).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(IReadOnlyList<ContextualChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var results = new EmbeddedChunk[chunks.Count];
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var consumers = Enumerable.Range(0, _options.Consumers)
            .Select(_ => ConsumeAsync(channel.Reader, chunks, results, stop))
            .ToList();
        var producer = ProduceAsync(channel.Writer, chunks.Count, stop.Token);

        // Consumers first: if one fails, its exception is the one reported, not the producer's cancellation.
        await Task.WhenAll([.. consumers, producer]).ConfigureAwait(false);
        return results;
    }

    private static async Task ProduceAsync(ChannelWriter<int> writer, int count, CancellationToken cancellationToken)
    {
        try
        {
            for (var index = 0; index < count; index++)
            {
                await writer.WriteAsync(index, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<int> reader,
        IReadOnlyList<ContextualChunk> chunks,
        EmbeddedChunk[] results,
        CancellationTokenSource stop)
    {
        try
        {
            await foreach (var index in reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                results[index] = await EmbedOneAsync(chunks[index], stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The document prefix, then the context and a blank line when there is one, then the chunk (P7, cookbook order).</summary>
    public string EmbeddingInput(ContextualChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return chunk.Context is null
            ? _options.DocumentPrefix + chunk.Chunk.Content
            : _options.DocumentPrefix + chunk.Context + "\n\n" + chunk.Chunk.Content;
    }

    private async Task<EmbeddedChunk> EmbedOneAsync(ContextualChunk contextual, CancellationToken cancellationToken)
    {
        var chunk = contextual.Chunk;
        var vector = await generator
            .GenerateVectorAsync(EmbeddingInput(contextual), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (vector.Length != _options.Dimensions)
        {
            throw new InvalidOperationException(
                $"Embedding for {chunk.DocumentId} '{chunk.ParaRef}' has {vector.Length} dimensions; expected {_options.Dimensions}. " +
                "Check the embedding model.");
        }

        return new EmbeddedChunk(chunk, vector, contextual.Context);
    }
}
