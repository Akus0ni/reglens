using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Application.Tests.Ingestion;

public class ChunkContextualizerTests
{
    private static readonly SourceDocument Document = new(
        "rbi-test", "rbi-test.pdf", "Test Directions", "RBI/TEST", "v1", "https://example.test", new string('a', 64),
        100, 1, new PageRange(1, 2), DocumentStructure.RbiDirections);

    private static readonly IReadOnlyList<PageText> Pages = [new(1, "Page one."), new(2, "Page two.")];

    private readonly FakeGenerator _generator = new();
    private readonly FakeCache _cache = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GeneratesAContextForEveryChunkInOrderAndSumsTheUsage()
    {
        var result = await Contextualizer().ContextualizeAsync(Document, Pages, Chunks(5), Ct);

        result.Chunks.Select(c => c.Context).Should().Equal(Enumerable.Range(1, 5).Select(i => $"Context for Para {i}."));
        result.Stats.Should().Match<ContextStats>(s => s.Generated == 5 && s.FromCache == 0
            && s.Usage == new TokenUsage(500, 0, 5000, 100));
        result.Stats.CostUsd.Should().BeApproximately(((500 * 0.10) + (5000 * 0.01) + (100 * 0.50)) / 1e6, 1e-12);
    }

    [Fact]
    public async Task SendsTheWholeDocumentTextWithEveryChunk() // P2
    {
        await Contextualizer().ContextualizeAsync(Document, Pages, Chunks(3), Ct);

        _generator.Requests.Select(r => r.DocumentText).Should().AllBe("Page one.\nPage two.");
    }

    [Fact]
    public async Task SendsTheFirstMissingChunkAloneThenTheRestInParallel() // P4
    {
        _generator.Delay = TimeSpan.FromMilliseconds(30);

        await Contextualizer(concurrency: 4).ContextualizeAsync(Document, Pages, Chunks(9), Ct);

        _generator.ConcurrentAtFirstCall.Should().Be(1, "the first response must write the prompt cache before others read it");
        _generator.MaxConcurrent.Should().BeInRange(2, 4);
    }

    [Fact]
    public async Task ReusesCachedContextsWithoutCallingTheModel() // R5.7, A4
    {
        var contextualizer = Contextualizer();
        await contextualizer.ContextualizeAsync(Document, Pages, Chunks(4), Ct);
        _generator.Requests.Clear();

        var again = await contextualizer.ContextualizeAsync(Document, Pages, Chunks(4), Ct);

        _generator.Requests.Should().BeEmpty();
        again.Stats.Should().Match<ContextStats>(s => s.Generated == 0 && s.FromCache == 4 && s.CostUsd == 0);
        again.Chunks.Select(c => c.Context).Should().Equal(Enumerable.Range(1, 4).Select(i => $"Context for Para {i}."));
    }

    [Fact]
    public async Task AFailureNamesTheChunkAndARerunPaysOnlyForWhatIsMissing() // R5.8, D54
    {
        _generator.FailOn = "Para 3";
        var contextualizer = Contextualizer(concurrency: 1);

        var act = () => contextualizer.ContextualizeAsync(Document, Pages, Chunks(4), Ct);

        await act.Should().ThrowAsync<ContextGenerationException>().WithMessage("*rbi-test 'Para 3'*not replaced*");
        _cache.Saved.Should().HaveCount(2, "Para 1 and Para 2 were saved as they arrived");

        _generator.FailOn = null;
        _generator.Requests.Clear();
        var rerun = await contextualizer.ContextualizeAsync(Document, Pages, Chunks(4), Ct);

        _generator.Requests.Select(r => r.Chunk.ParaRef).Should().BeEquivalentTo("Para 3", "Para 4");
        rerun.Stats.Should().Match<ContextStats>(s => s.Generated == 2 && s.FromCache == 2);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task AnEmptyContextIsAFailure(string text) // R5.9
    {
        _generator.Reply = _ => text;

        var act = () => Contextualizer().ContextualizeAsync(Document, Pages, Chunks(1), Ct);

        await act.Should().ThrowAsync<ContextGenerationException>().WithMessage("*0 characters*");
        _cache.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AContextOverTheLimitIsAFailure() // R5.9
    {
        _generator.Reply = _ => new string('x', ChunkContextualizer.MaxContextCharacters + 1);

        var act = () => Contextualizer().ContextualizeAsync(Document, Pages, Chunks(1), Ct);

        await act.Should().ThrowAsync<ContextGenerationException>().WithMessage("*2001 characters*");
    }

    [Fact]
    public async Task TrimsTheContextBeforeStoringIt()
    {
        _generator.Reply = _ => "\n  Situated context.  \n";

        var result = await Contextualizer().ContextualizeAsync(Document, Pages, Chunks(1), Ct);

        result.Chunks.Single().Context.Should().Be("Situated context.");
        _cache.Saved.Single().Context.Should().Be("Situated context.");
    }

    [Fact]
    public void TheInputHashChangesWithEverythingThatShapesTheContext() // P5
    {
        var chunk = Chunks(1)[0];
        var documentHash = ChunkContextualizer.Sha256("document");
        var baseline = Contextualizer().InputHash(documentHash, chunk);

        new[]
        {
            Contextualizer(model: "claude-haiku-4-5").InputHash(documentHash, chunk),
            Contextualizer().InputHash(ChunkContextualizer.Sha256("document v2"), chunk),
            Contextualizer().InputHash(documentHash, chunk with { Heading = "Chapter II" }),
            Contextualizer().InputHash(documentHash, chunk with { ParaRef = "Para 9" }),
            Contextualizer().InputHash(documentHash, chunk with { Content = "1. Changed." }),
        }.Should().OnlyHaveUniqueItems().And.NotContain(baseline);
        Contextualizer().InputHash(documentHash, chunk).Should().Be(baseline);
    }

    private ChunkContextualizer Contextualizer(int concurrency = 4, string model = "claude-haiku-5-5") => new(
        _generator, _cache, Options.Create(new ContextOptions { Concurrency = concurrency, Model = model }),
        NullLogger<ChunkContextualizer>.Instance);

    private static List<Chunk> Chunks(int count) => Enumerable.Range(1, count)
        .Select(i => new Chunk(Document.Id, $"Para {i}", "Chapter I – Preliminary", 1, 1, $"{i}. Paragraph {i}."))
        .ToList();

    private sealed class FakeGenerator : IChunkContextGenerator
    {
        private int _running;

        public List<ContextRequest> Requests { get; } = [];

        public TimeSpan Delay { get; set; }

        public string? FailOn { get; set; }

        public Func<ContextRequest, string> Reply { get; set; } = r => $"Context for {r.Chunk.ParaRef}.";

        public int? ConcurrentAtFirstCall { get; private set; }

        public int MaxConcurrent { get; private set; }

        public async Task<GeneratedContext> GenerateAsync(ContextRequest request, CancellationToken cancellationToken)
        {
            var running = Interlocked.Increment(ref _running);
            lock (Requests)
            {
                Requests.Add(request);
                ConcurrentAtFirstCall ??= running;
                MaxConcurrent = Math.Max(MaxConcurrent, running);
            }

            try
            {
                await Task.Delay(Delay, cancellationToken);
                return request.Chunk.ParaRef == FailOn
                    ? throw new HttpRequestException("overloaded")
                    : new GeneratedContext(Reply(request), new TokenUsage(100, 0, 1000, 20));
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private sealed class FakeCache : IChunkContextCache
    {
        private readonly Dictionary<string, string> _rows = [];

        public List<CachedContext> Saved { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> GetAsync(IReadOnlyCollection<string> inputHashes, CancellationToken cancellationToken)
        {
            lock (_rows)
            {
                IReadOnlyDictionary<string, string> found = inputHashes.Where(_rows.ContainsKey).ToDictionary(h => h, h => _rows[h]);
                return Task.FromResult(found);
            }
        }

        public Task SaveAsync(CachedContext context, CancellationToken cancellationToken)
        {
            lock (_rows)
            {
                Saved.Add(context);
                _rows.TryAdd(context.InputHash, context.Context);
            }

            return Task.CompletedTask;
        }
    }
}
