using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Application.Tests.Search;

public class SearchRegulationsTests
{
    private readonly IChunkSearch _search = Substitute.For<IChunkSearch>();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
    private readonly List<string> _embedded = [];

    public SearchRegulationsTests()
    {
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _embedded.AddRange(call.Arg<IEnumerable<string>>());
                return new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]);
            });
        _search.HybridAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([Hit(1, 0.03), Hit(2, 0.02), Hit(3, 0.01)]);
        _search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task EmbedsTheQuestionWithTheQueryPrefix()
    {
        await Run("What is a Key Fact Statement?");

        _embedded.Should().Equal("search_query: What is a Key Fact Statement?");
    }

    [Fact]
    public async Task ReturnsTheFusedRankingWhenNothingIsCited()
    {
        var result = await Run("What is a Key Fact Statement?");

        result.Hits.Select(h => h.ChunkId).Should().Equal(1, 2, 3);
        result.Degraded.Should().BeNull();
        await _search.DidNotReceiveWithAnyArgs().FindByCitationsAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PutsCitedMatchesFirstWithoutDuplicates()
    {
        _search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit(9, 0.0), Hit(2, 0.02)]);

        var result = await Run("Rule 7");

        result.Hits.Select(h => h.ChunkId).Should().Equal(2, 9, 1, 3);
        result.Hits.Select(h => h.Pinned).Should().Equal(true, true, false, false);
    }

    [Fact]
    public async Task SkipsTheEmbeddingInKeywordOnlyMode()
    {
        var result = await Search(new SearchOptions { Mode = SearchMode.KeywordOnly, KeywordScoring = KeywordScoring.Bm25 })
            .RunAsync("Rule 7", 8, TestContext.Current.CancellationToken);

        _embedded.Should().BeEmpty();
        result.Degraded.Should().BeNull();
        await _search.Received(1).HybridAsync(
            Arg.Is<SearchQuery>(q => !q.HasVector && q.UseKeywords && q.Scoring == KeywordScoring.Bm25), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TurnsTheKeywordSideOffInDenseOnlyMode()
    {
        await Search(new SearchOptions { Mode = SearchMode.DenseOnly }).RunAsync("Rule 7", 8, TestContext.Current.CancellationToken);

        await _search.Received(1).HybridAsync(Arg.Is<SearchQuery>(q => q.HasVector && !q.UseKeywords), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopsAtTopK()
    {
        var result = await Run("What is a Key Fact Statement?", topK: 2);

        result.Hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task FallsBackToKeywordSearchWhenTheEmbeddingFails()
    {
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Ollama is down"));

        var result = await Run("When must a breach be reported?");

        result.Degraded.Should().Be(SearchResult.VectorSearchUnavailable);
        await _search.Received(1).HybridAsync(Arg.Is<SearchQuery>(q => !q.HasVector), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LetsTheCallersCancellationPropagate()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        var act = () => Search().RunAsync("Rule 7", 8, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private Task<SearchResult> Run(string question, int topK = 8) =>
        Search().RunAsync(question, topK, TestContext.Current.CancellationToken);

    private SearchRegulations Search(SearchOptions? options = null) =>
        new(_search, _generator, Options.Create(options ?? new SearchOptions()), NullLogger<SearchRegulations>.Instance);

    private static SearchHit Hit(long id, double score) =>
        new(id, "doc", "Title", $"Para {id}", "", 1, 1, "excerpt", 1, 1, score);
}
