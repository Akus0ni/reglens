using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;

namespace RegLens.Application.Tests.Ingestion;

public class ChunkEmbedderTests
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();

    [Fact]
    public async Task ReturnsOneVectorPerChunkInTheOriginalOrder()
    {
        // The vector's first value encodes which chunk it came from, so order can be checked across consumers.
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => Vector(float.Parse(call.Arg<IEnumerable<string>>().Single().Split('#')[1], System.Globalization.CultureInfo.InvariantCulture), 768));
        var chunks = Enumerable.Range(0, 50).Select(i => Chunk($"chunk #{i}")).ToList();

        var embedded = await Embedder(consumers: 4, capacity: 2).EmbedAsync(chunks, TestContext.Current.CancellationToken);

        embedded.Select(e => e.Vector.Span[0]).Should().Equal(Enumerable.Range(0, 50).Select(i => (float)i));
    }

    [Fact]
    public async Task FailsWhenTheModelReturnsTheWrongNumberOfDimensions()
    {
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Vector(0, 384));

        var act = () => Embedder().EmbedAsync([Chunk("text")], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*384 dimensions; expected 768*");
    }

    [Fact]
    public async Task ReportsTheConsumersErrorRatherThanACancellation()
    {
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns<GeneratedEmbeddings<Embedding<float>>>(_ => throw new HttpRequestException("Ollama is down"));
        var chunks = Enumerable.Range(0, 100).Select(i => Chunk($"chunk {i}")).ToList();

        var act = () => Embedder(consumers: 2, capacity: 1).EmbedAsync(chunks, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Ollama is down");
    }

    private ChunkEmbedder Embedder(int consumers = 2, int capacity = 32) =>
        new(_generator, Options.Create(new EmbeddingOptions { Consumers = consumers, ChannelCapacity = capacity }));

    private static GeneratedEmbeddings<Embedding<float>> Vector(float first, int dimensions)
    {
        var values = new float[dimensions];
        values[0] = first;
        return new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(values)]);
    }

    private static Chunk Chunk(string content) => new("doc", "Para 1", "", 1, 1, content);
}
