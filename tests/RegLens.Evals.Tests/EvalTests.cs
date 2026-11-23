using Microsoft.Extensions.AI;
using NSubstitute;
using RegLens.Application.Search;

namespace RegLens.Evals.Tests;

public class GoldenSetTests
{
    [Fact]
    public void TheCommittedGoldenSetIsValid()
    {
        var set = GoldenSet.Load(Path.Combine(AppContext.BaseDirectory, "golden-set.json"));

        set.Answerable.Should().HaveCount(GoldenSet.AnswerableCount);
        set.Unanswerable.Should().HaveCount(GoldenSet.UnanswerableCount);
    }

    [Fact]
    public void ReportsDuplicateIdsWrongCountsAndMissingReferences()
    {
        GoldenQuestion[] questions =
        [
            new("a", true, "direct", "q?", [new ExpectedSource("doc", ["Para 1"])]),
            new("a", true, "direct", "q?", []),
            new("b", false, "out-of-domain", "q?", [new ExpectedSource("doc", ["Para 2"])]),
        ];

        var errors = new GoldenSet(1, "2026-11-23", "draft", "", questions).Validate();

        errors.Should().Contain(e => e.Contains("Duplicate id a", StringComparison.Ordinal))
            .And.Contain(e => e.Contains("Expected 20 answerable", StringComparison.Ordinal))
            .And.Contain(e => e.Contains("a is answerable but names no expected reference", StringComparison.Ordinal))
            .And.Contain(e => e.Contains("b is unanswerable but names expected references", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Para 13", "Para 13", true)]
    [InlineData("Para 13", "Para 13, part 1 of 2", true)]
    [InlineData("Para 13", "Para 130", false)]
    [InlineData("Annex I", "Annex I, part 2 of 2", true)]
    [InlineData("Annex I", "Annex II", false)]
    [InlineData("Third Schedule", "Third Schedule, Item 4", true)]
    public void MatchesAReferenceAndItsParts(string reference, string paraRef, bool matches) =>
        GoldenSet.Matches(reference, paraRef).Should().Be(matches);

    [Fact]
    public void MatchesOnlyWithinTheExpectedDocument()
    {
        var question = new GoldenQuestion("cf-04", true, "citation", "q?", [new ExpectedSource("credit", ["Para 18"])]);

        GoldenSet.Matches(question, "credit", "Para 18").Should().BeTrue();
        GoldenSet.Matches(question, "kyc", "Para 18").Should().BeFalse();
    }
}

public class MetricsTests
{
    private static readonly int?[] Ranks = [1, 2, 6, null, 11];

    [Fact]
    public void ComputesRecallAtK() => Metrics.RecallAt(Ranks, 5).Should().BeApproximately(2.0 / 5, 1e-9);

    [Fact]
    public void ComputesMeanReciprocalRankWithACutoff() =>
        Metrics.MeanReciprocalRank(Ranks, 10).Should().BeApproximately((1 + 0.5 + (1.0 / 6)) / 5, 1e-9);

    [Fact]
    public void TakesThePercentileByNearestRank() => Metrics.Percentile([4, 1, 3, 2], 50).Should().Be(2);

    [Theory]
    [InlineData(new[] { 0.623, 0.70, 0.91 }, 0.60)]
    [InlineData(new[] { 0.57, 0.80 }, 0.55)]
    [InlineData(new[] { 0.01 }, 0)]
    public void SetsTheFloorJustBelowTheLowestAnswerableQuestion(double[] similarities, double floor) =>
        Metrics.Floor(similarities, margin: 0.02).Should().BeApproximately(floor, 1e-9);
}

public class EvalCommandLineTests
{
    [Fact]
    public void ParsesTheCommandConfigurationFloorAndSpendFlag()
    {
        var command = EvalCommandLine.Parse(["answers", "--config", "hybrid-bm25", "--floor", "0.6", "--confirm-spend"]);

        command.Name.Should().Be("answers");
        command.Config.Should().Be(new EvalConfiguration("hybrid-bm25", SearchMode.Hybrid, KeywordScoring.Bm25));
        command.Floor.Should().Be(0.6);
        command.ConfirmSpend.Should().BeTrue();
        command.Spends.Should().BeTrue();
    }

    [Theory]
    [InlineData("retrieval", false)]
    [InlineData("answers", true)]
    [InlineData("probe", true)]
    public void KnowsWhichCommandsSpendMoney(string name, bool spends) => EvalCommandLine.Parse([name]).Spends.Should().Be(spends);

    [Theory]
    [InlineData("nonsense")]
    [InlineData("retrieval", "--config", "sparse")]
    [InlineData("answers", "--floor", "2")]
    [InlineData("answers", "--verbose")]
    public void RejectsBadArguments(params string[] args) =>
        FluentActions.Invoking(() => EvalCommandLine.Parse(args)).Should().Throw<ArgumentException>();
}

public class JudgeTests
{
    [Fact]
    public async Task DropsTopPAndKeepsTemperature()
    {
        var inner = Substitute.For<IChatClient>();
        ChatOptions? sent = null;
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions?>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var original = new ChatOptions { Temperature = 0, TopP = 1 };

        await new DropTopP(inner).GetResponseAsync([new ChatMessage(ChatRole.User, "q")], original, TestContext.Current.CancellationToken);

        sent!.TopP.Should().BeNull();
        sent.Temperature.Should().Be(0);
        original.TopP.Should().Be(1, "the caller's options are not changed");
    }

    [Fact]
    public async Task ReadsScoresAndCountsTheJudgesTokens()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "<S0>thinking</S0><S1>Fully supported.</S1><S2>5</S2>"))
            {
                Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 50 },
            });
        var judge = new Judge(client);

        var scores = await judge.ScoreAsync("When must a breach be reported?", "Without delay.", "Rule 7 text", TestContext.Current.CancellationToken);

        scores.Should().HaveCount(2).And.AllSatisfy(s =>
        {
            s.Score.Should().Be(5);
            s.Failed.Should().BeFalse();
        });
        judge.InputTokens.Should().Be(2000);
        judge.OutputTokens.Should().Be(100);
    }
}
