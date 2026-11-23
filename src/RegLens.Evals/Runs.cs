using RegLens.Application.Search;

namespace RegLens.Evals;

/// <summary>A named retrieval setup (spec 0004, P1).</summary>
public sealed record EvalConfiguration(string Name, SearchMode Mode, KeywordScoring Scoring)
{
    public static IReadOnlyList<EvalConfiguration> All { get; } =
    [
        new("dense", SearchMode.DenseOnly, KeywordScoring.TsRankCd),
        new("keyword-tsrank", SearchMode.KeywordOnly, KeywordScoring.TsRankCd),
        new("keyword-bm25", SearchMode.KeywordOnly, KeywordScoring.Bm25),
        new("hybrid-tsrank", SearchMode.Hybrid, KeywordScoring.TsRankCd),
        new("hybrid-bm25", SearchMode.Hybrid, KeywordScoring.Bm25),
    ];

    public static EvalConfiguration Find(string name) =>
        All.SingleOrDefault(c => c.Name == name)
        ?? throw new ArgumentException($"Unknown configuration '{name}'. Use one of: {string.Join(", ", All.Select(c => c.Name))}.");
}

/// <summary>What every run records about where its numbers came from (P10).</summary>
public sealed record RunInfo(string Kind, string Config, DateTimeOffset StartedUtc, string Commit, string EmbeddingModel, string GoldenSetVersion);

public sealed record RetrievalQuestion(
    string Id,
    string Kind,
    int? FirstHitRank,
    double? BestSimilarityTop8,
    bool Pinned,
    IReadOnlyList<string> Top5);

public sealed record UnanswerableSimilarity(string Id, string Kind, double? BestSimilarityTop8);

public sealed record RetrievalRun(
    RunInfo Info,
    int N,
    double RecallAt5,
    double RecallAt8,
    double MrrAt10,
    IReadOnlyList<RetrievalQuestion> Questions,
    IReadOnlyList<UnanswerableSimilarity> Unanswerable);

public sealed record JudgeScore(string Metric, double? Score, string? Reason, bool Failed);

public sealed record AnswerQuestionResult(
    string Id,
    bool Answerable,
    string Question,
    bool Abstained,
    string? AbstainReason,
    string Answer,
    string MissingInfo,
    IReadOnlyList<string> CitedRefs,
    bool CitesExpected,
    int DroppedCitations,
    long InputTokens,
    long OutputTokens,
    double LatencyMs,
    IReadOnlyList<JudgeScore> Judgements);

public sealed record AnswerRun(
    RunInfo Info,
    string Model,
    string Effort,
    string JudgeModel,
    double SimilarityFloor,
    int N,
    int UnanswerableAbstained,
    int AnswerableAbstained,
    int AnsweredAnswerable,
    int AnsweredCitingExpected,
    int CitationsDropped,
    double? GroundednessMean,
    double? RelevanceMean,
    int JudgeFailures,
    long InputTokens,
    long OutputTokens,
    long JudgeInputTokens,
    long JudgeOutputTokens,
    double CostUsd,
    double LatencyP50Ms,
    IReadOnlyList<AnswerQuestionResult> Questions);

public sealed record ProbeRun(RunInfo Info, string Question, bool Passed, bool CanaryCited, bool Abstained, string Answer, double CostUsd);

/// <summary>List prices per million tokens (Claude pricing page, checked 2026-11-23).</summary>
public static class Pricing
{
    public static double Cost(string model, long inputTokens, long outputTokens) => model switch
    {
        "claude-sonnet-5-5" => ((inputTokens * 2.0) + (outputTokens * 10.0)) / 1_000_000,
        "claude-haiku-4-5" => ((inputTokens * 1.0) + (outputTokens * 5.0)) / 1_000_000,
        _ => throw new ArgumentException($"No price recorded for {model}."),
    };
}
