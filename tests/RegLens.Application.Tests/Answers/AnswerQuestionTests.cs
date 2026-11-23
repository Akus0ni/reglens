using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RegLens.Application.Answers;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Application.Tests.Answers;

/// <summary>The answer use case with search, passages and the model substituted, and a real in-memory HybridCache.</summary>
public sealed class AnswerQuestionTests : IDisposable
{
    private const string Rule7Text = "7. Intimation of personal data breach. On becoming aware of any personal data breach, the Data Fiduciary shall intimate the Board without delay.";
    private const string Quote = "the Data Fiduciary shall intimate the Board without delay";

    private readonly IChunkSearch _search = Substitute.For<IChunkSearch>();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
    private readonly IPassageReader _passages = Substitute.For<IPassageReader>();
    private readonly IChatClient _chat = Substitute.For<IChatClient>();
    private readonly IIndexVersion _indexVersion = Substitute.For<IIndexVersion>();
    private readonly ServiceProvider _provider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
    private readonly List<ChatOptions?> _sentOptions = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AnswerQuestionTests()
    {
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]));
        Hits(Hit(42, similarity: 0.77), Hit(7, similarity: 0.70));
        _search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        _passages.GetAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns(
        [
            new StoredPassage(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", 26, 26, Rule7Text),
            new StoredPassage(7, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 6", 25, 25, "6. Reasonable security safeguards."),
        ]);
        _indexVersion.GetAsync(Arg.Any<CancellationToken>()).Returns("index-a");
        ModelReplies($$"""{"answer":"Without delay.","abstained":false,"missingInfo":"","citations":[{"chunkId":42,"quote":"{{Quote}}"}]}""");
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task AnswersWithVerifiedCitationsAndTheirSource()
    {
        var result = await Run();

        result.Abstained.Should().BeFalse();
        result.Answer.Should().Be("Without delay.");
        result.Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new AnswerCitation(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", 26, 26, Quote));
        result.Cached.Should().BeFalse();
        result.Usage.Should().Be(new AnswerUsage(1200, 300));
        result.Model.Should().Be("claude-sonnet-5-5");
    }

    [Fact]
    public async Task SendsTheSchemaEffortAndTokenLimit()
    {
        await Run();

        var options = _sentOptions.Should().ContainSingle().Subject!;
        options.ResponseFormat.Should().BeSameAs(AnswerPrompt.ResponseFormat);
        options.Reasoning!.Effort.Should().Be(ReasoningEffort.Medium);
        options.MaxOutputTokens.Should().Be(8192);
    }

    [Fact]
    public async Task AbstainsBelowTheSimilarityFloorWithoutCallingTheModel()
    {
        Hits(Hit(42, similarity: 0.54));

        var result = await Run("Who won the cricket world cup?");

        result.Abstained.Should().BeTrue();
        result.AbstainReason.Should().Be(AnswerResult.LowSimilarity);
        result.Usage.Should().BeNull();
        ModelCalls(0);
    }

    [Fact]
    public async Task SendsACitedParagraphToTheModelWhateverItsSimilarity()
    {
        Hits(Hit(42, similarity: 0.30));
        _search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit(42, similarity: 0.30)]);

        var result = await Run("What does Rule 7 say?");

        result.Abstained.Should().BeFalse();
        ModelCalls(1);
    }

    [Fact]
    public async Task AbstainsWhenTheModelSaysThePassagesDoNotAnswer()
    {
        ModelReplies("""{"answer":"","abstained":true,"missingInfo":"GST rates are not in these passages.","citations":[]}""");

        var result = await Run("What is the GST rate on gold?");

        result.Abstained.Should().BeTrue();
        result.AbstainReason.Should().Be(AnswerResult.ModelAbstained);
        result.MissingInfo.Should().Be("GST rates are not in these passages.");
        result.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task AbstainsAsUnverifiedWhenNoCitationSurvives()
    {
        ModelReplies("""{"answer":"Within 72 hours.","abstained":false,"missingInfo":"","citations":[{"chunkId":999,"quote":"within seventy-two hours of becoming aware"},{"chunkId":7,"quote":"the Data Fiduciary shall intimate the Board"}]}""");

        var result = await Run();

        result.Abstained.Should().BeTrue();
        result.AbstainReason.Should().Be(AnswerResult.Unverified);
        result.Answer.Should().BeEmpty();
        result.DroppedCitations.Should().Be(2);
    }

    [Fact]
    public async Task KeepsTheAnswerAndCountsTheCitationsItDrops()
    {
        ModelReplies($$"""{"answer":"Without delay.","abstained":false,"missingInfo":"","citations":[{"chunkId":42,"quote":"{{Quote}}"},{"chunkId":999,"quote":"a forged quote that was never retrieved"}]}""");

        var result = await Run();

        result.Abstained.Should().BeFalse();
        result.Citations.Should().ContainSingle();
        result.DroppedCitations.Should().Be(1);
    }

    [Fact]
    public async Task TurnsARefusalIntoAnAbstentionThatIsNotCached()
    {
        ModelReplies("I can't help with that.", ChatFinishReason.ContentFilter);

        var first = await Run();
        var second = await Run();

        first.AbstainReason.Should().Be(AnswerResult.ModelRefused);
        second.Cached.Should().BeFalse();
        ModelCalls(2);
    }

    [Fact]
    public async Task FailsWhenTheModelRunsOutOfTokens()
    {
        ModelReplies("""{"answer":"Within""", ChatFinishReason.Length);

        var act = () => Run();

        (await act.Should().ThrowAsync<AnswerGenerationException>()).Which.Failure.Should().Be(AnswerFailure.InvalidModelOutput);
    }

    [Fact]
    public async Task FailsWhenTheOutputDoesNotFitTheSchema()
    {
        ModelReplies("Here is your answer: within 72 hours.");

        var act = () => Run();

        (await act.Should().ThrowAsync<AnswerGenerationException>()).Which.Failure.Should().Be(AnswerFailure.InvalidModelOutput);
    }

    [Fact]
    public async Task ServesARepeatedQuestionFromTheCache()
    {
        await Run("When must a breach be reported?");
        var again = await Run("  when must a BREACH   be reported? ");

        again.Cached.Should().BeTrue();
        again.Usage.Should().BeNull();
        again.Citations.Should().ContainSingle();
        ModelCalls(1);
    }

    [Fact]
    public async Task AsksAgainAfterAReingest()
    {
        await Run();
        _indexVersion.GetAsync(Arg.Any<CancellationToken>()).Returns("index-b");

        var after = await Run();

        after.Cached.Should().BeFalse();
        ModelCalls(2);
    }

    [Fact]
    public async Task AnswersFromKeywordHitsWhenVectorSearchIsDownWithoutCaching()
    {
        _generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Ollama is down"));
        Hits(Hit(42, similarity: null));

        var first = await Run();
        var second = await Run();

        first.Degraded.Should().Be(SearchResult.VectorSearchUnavailable);
        first.Abstained.Should().BeFalse();
        second.Cached.Should().BeFalse();
        ModelCalls(2);
    }

    [Fact]
    public async Task CallsTheModelOnceForConcurrentIdenticalQuestions()
    {
        var release = new TaskCompletionSource();
        var reply = _chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        reply.Returns(async _ =>
        {
            await release.Task;
            return Reply($$"""{"answer":"Without delay.","abstained":false,"missingInfo":"","citations":[{"chunkId":42,"quote":"{{Quote}}"}]}""");
        });

        var answers = Enumerable.Range(0, 10).Select(_ => Run()).ToList();
        await Task.Delay(200, Ct);
        release.SetResult();
        var results = await Task.WhenAll(answers);

        results.Should().AllSatisfy(r => r.Citations.Should().ContainSingle());
        results.Count(r => !r.Cached).Should().Be(1);
        ModelCalls(1);
    }

    [Fact]
    public async Task LetsTheCallersCancellationPropagate()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => Answers().RunAsync("When must a breach be reported?", 8, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void BuildsTheCacheKeyFromModelEffortIndexTopKAndTheNormalisedQuestion()
    {
        var key = Answers().CacheKey(" Rule  7 ", 8, "index-a");

        key.Should().StartWith("answer:v1:claude-sonnet-5-5:Medium:index-a:8:")
            .And.Be(Answers().CacheKey("rule 7", 8, "index-a"))
            .And.NotBe(Answers().CacheKey("rule 7", 5, "index-a"));
        key.Should().NotContain("rule");
    }

    private Task<AnswerResult> Run(string question = "When must a breach be reported?") => Answers().RunAsync(question, 8, Ct);

    private AnswerQuestion Answers() => new(
        new SearchRegulations(_search, _generator, Options.Create(new SearchOptions()), NullLogger<SearchRegulations>.Instance),
        _passages,
        _chat,
        _provider.GetRequiredService<HybridCache>(),
        _indexVersion,
        Options.Create(new AnswerOptions()),
        NullLogger<AnswerQuestion>.Instance);

    private void Hits(params SearchHit[] hits) =>
        _search.HybridAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns(hits);

    private void ModelReplies(string text, ChatFinishReason? finish = null) =>
        _chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sentOptions.Add(call.Arg<ChatOptions?>());
                return Reply(text, finish ?? ChatFinishReason.Stop);
            });

    private static ChatResponse Reply(string text, ChatFinishReason? finish = null) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finish ?? ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 300 },
        };

    private void ModelCalls(int count) => _ =
        _chat.Received(count).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());

    private static SearchHit Hit(long id, double? similarity) =>
        new(id, "dpdp-rules-2025", "DPDP Rules, 2025", $"Rule {id}", "", 1, 1, "excerpt", 1, 1, 0.03, similarity);
}
