using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RegLens.Domain.Chunking;

namespace RegLens.Application.Ingestion;

/// <summary>
/// Embeds chunks through a bounded channel with a fixed number of consumers (spec 0001, R1.18–R1.19).
/// The channel is the backpressure: when Ollama is slow, the producer waits instead of queueing everything in memory.
/// </summary>
public sealed class ChunkEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<EmbeddingOptions> options)
{
    private readonly EmbeddingOptions _options = options.Value;

    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken)
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
        IReadOnlyList<Chunk> chunks,
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

    private async Task<EmbeddedChunk> EmbedOneAsync(Chunk chunk, CancellationToken cancellationToken)
    {
        var vector = await generator
            .GenerateVectorAsync(_options.DocumentPrefix + chunk.Content, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (vector.Length != _options.Dimensions)
        {
            throw new InvalidOperationException(
                $"Embedding for {chunk.DocumentId} '{chunk.ParaRef}' has {vector.Length} dimensions; expected {_options.Dimensions}. " +
                "Check the embedding model.");
        }

        return new EmbeddedChunk(chunk, vector);
    }
}
