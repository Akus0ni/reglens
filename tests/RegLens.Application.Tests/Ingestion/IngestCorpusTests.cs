using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Application.Tests.Ingestion;

public class IngestCorpusTests
{
    private static readonly SourceDocument Document = new(
        "rbi-test", "rbi-test.pdf", "Test Directions", "RBI/TEST", "v1", "https://example.test", new string('a', 64),
        100, 1, new PageRange(1, 1), DocumentStructure.RbiDirections);

    private readonly ICorpusCatalog _catalog = Substitute.For<ICorpusCatalog>();
    private readonly IDocumentTextSource _textSource = Substitute.For<IDocumentTextSource>();
    private readonly IChunkStore _store = Substitute.For<IChunkStore>();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
    private readonly List<string> _embeddedInputs = [];

    public IngestCorpusTests()
    {
        _catalog.LoadAsync(Arg.Any<CancellationToken>()).Returns([Document]);
        _textSource.ReadPages(Document).Returns([new PageText(1, "1. First paragraph.\n2. Second paragraph.")]);
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (_embeddedInputs)
                {
                    _embeddedInputs.AddRange(call.Arg<IEnumerable<string>>());
                }

                return new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]);
            });
    }

    [Fact]
    public async Task SkipsADocumentThatIsAlreadyCurrent()
    {
        StoredState(StoredDocumentState.Current);

        var report = await Run();

        report.Documents.Should().ContainSingle().Which.Status.Should().Be(IngestStatus.Skipped);
        _textSource.DidNotReceiveWithAnyArgs().ReadPages(default!);
        await _store.DidNotReceiveWithAnyArgs().ReplaceDocumentAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RefreshesKeywordStatisticsOnlyWhenSomethingWasWritten()
    {
        StoredState(StoredDocumentState.Current);
        await Run();
        await _store.DidNotReceiveWithAnyArgs().RefreshSearchStatisticsAsync(TestContext.Current.CancellationToken);

        StoredState(StoredDocumentState.Missing);
        await Run();
        await _store.Received(1).RefreshSearchStatisticsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestsANewDocument()
    {
        StoredState(StoredDocumentState.Missing);

        var report = await Run();

        report.Documents.Should().ContainSingle().Which.Should().Match<DocumentIngestResult>(r =>
            r.Status == IngestStatus.Ingested && r.ChunksWritten == 2 && r.PagesRead == 1);
        await _store.Received(1).ReplaceDocumentAsync(Document, Arg.Is<IReadOnlyList<EmbeddedChunk>>(c => c.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplacesADocumentWhoseFileChanged()
    {
        StoredState(StoredDocumentState.Outdated);

        var report = await Run();

        report.Documents.Should().ContainSingle().Which.Status.Should().Be(IngestStatus.Replaced);
    }

    [Fact]
    public async Task ReplacesACurrentDocumentWhenForced()
    {
        StoredState(StoredDocumentState.Current);

        var report = await Run(new IngestRequest(Force: true));

        report.Documents.Should().ContainSingle().Which.Status.Should().Be(IngestStatus.Replaced);
    }

    [Fact]
    public async Task WritesNothingWhenTheCorpusFailsItsIntegrityCheck()
    {
        _catalog.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new CorpusIntegrityException("SHA-256 mismatch for rbi-test"));

        var act = () => Run();

        await act.Should().ThrowAsync<CorpusIntegrityException>().WithMessage("*rbi-test*");
        await _store.DidNotReceiveWithAnyArgs().GetStateAsync(default!, TestContext.Current.CancellationToken);
        await _store.DidNotReceiveWithAnyArgs().ReplaceDocumentAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EmbedsChunksWithTheDocumentTaskPrefix()
    {
        StoredState(StoredDocumentState.Missing);

        await Run();

        _embeddedInputs.Should().HaveCount(2).And.AllSatisfy(input => input.Should().MatchRegex(@"^search_document: [12]\. "));
    }

    [Fact]
    public async Task RejectsAnUnknownDocumentId()
    {
        var act = () => Run(new IngestRequest(OnlyDocumentId: "nope"));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*nope*rbi-test*");
    }

    private void StoredState(StoredDocumentState state) =>
        _store.GetStateAsync(Document, Arg.Any<CancellationToken>()).Returns(state);

    private Task<IngestReport> Run(IngestRequest? request = null)
    {
        var useCase = new IngestCorpus(
            _catalog,
            _textSource,
            DocumentChunker.CreateDefault(),
            new ChunkEmbedder(_generator, Options.Create(new EmbeddingOptions())),
            _store,
            NullLogger<IngestCorpus>.Instance);
        return useCase.RunAsync(request ?? new IngestRequest(), TestContext.Current.CancellationToken);
    }
}
