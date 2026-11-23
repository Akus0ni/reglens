using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace RegLens.Evals;

/// <summary>
/// Claude 4.x accepts <c>temperature</c> or <c>top_p</c>, not both, and Microsoft's quality evaluators set both
/// (<c>Temperature = 0</c>, <c>TopP = 1</c>). Dropping <c>TopP</c> keeps the deterministic temperature (spec 0004, P5).
/// </summary>
public sealed class DropTopP(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Strip(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Strip(options), cancellationToken);

    private static ChatOptions? Strip(ChatOptions? options)
    {
        if (options?.TopP is null)
        {
            return options;
        }

        var copy = options.Clone();
        copy.TopP = null;
        return copy;
    }
}

/// <summary>Groundedness and relevance of an answer, judged by Haiku (spec 0004, D47).</summary>
public sealed class Judge(IChatClient judgeClient)
{
    private readonly ChatConfiguration _configuration = new(judgeClient);
    private readonly GroundednessEvaluator _groundedness = new();
    private readonly RelevanceEvaluator _relevance = new();

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    /// <param name="groundingContext">The passages the answer model was given.</param>
    public async Task<IReadOnlyList<JudgeScore>> ScoreAsync(
        string question, string answer, string groundingContext, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = [new(ChatRole.User, question)];
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));

        var grounded = await _groundedness.EvaluateAsync(messages, response, _configuration,
            [new GroundednessEvaluatorContext(groundingContext)], cancellationToken).ConfigureAwait(false);
        var relevant = await _relevance.EvaluateAsync(messages, response, _configuration,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return
        [
            Read(grounded, GroundednessEvaluator.GroundednessMetricName),
            Read(relevant, RelevanceEvaluator.RelevanceMetricName),
        ];
    }

    private JudgeScore Read(EvaluationResult result, string name)
    {
        var metric = result.Get<NumericMetric>(name);
        if (metric.Metadata is { } metadata)
        {
            InputTokens += Tokens(metadata, "eval-input-tokens");
            OutputTokens += Tokens(metadata, "eval-output-tokens");
        }

        var failed = metric.Value is null || metric.Diagnostics?.Any(d => d.Severity == EvaluationDiagnosticSeverity.Error) == true;
        return new JudgeScore(name, metric.Value, metric.Reason, failed);
    }

    private static long Tokens(IDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && long.TryParse(value, out var tokens) ? tokens : 0;
}
