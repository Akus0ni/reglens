namespace RegLens.Application;

/// <summary>Tokens one or more Claude calls used, split the way they are billed (spec 0005, P10).</summary>
public sealed record TokenUsage(long InputTokens, long CacheWriteTokens, long CacheReadTokens, long OutputTokens)
{
    public static TokenUsage None { get; } = new(0, 0, 0, 0);

    public static TokenUsage operator +(TokenUsage left, TokenUsage right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new TokenUsage(
            left.InputTokens + right.InputTokens,
            left.CacheWriteTokens + right.CacheWriteTokens,
            left.CacheReadTokens + right.CacheReadTokens,
            left.OutputTokens + right.OutputTokens);
    }

    public static TokenUsage Add(TokenUsage left, TokenUsage right) => left + right;
}

/// <summary>
/// List prices per million tokens (https://platform.claude.com/docs/en/about-claude/pricing; Haiku 5.5 from its
/// overview page; checked 2026-11-24). Cache writes are the 5-minute TTL price. Every prompt here is under Haiku 5.5's
/// 100k-token threshold, above which its prices are 5×.
/// </summary>
public static class ModelPricing
{
    /// <summary>Cost of calls that used no prompt cache.</summary>
    public static double Cost(string model, long inputTokens, long outputTokens) =>
        Cost(model, new TokenUsage(inputTokens, 0, 0, outputTokens));

    /// <summary>Cost in US dollars; <see cref="TokenUsage.InputTokens"/> are the uncached input tokens only.</summary>
    public static double Cost(string model, TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var (input, write, read, output) = model switch
        {
            "claude-sonnet-5-5" => (2.0, 2.5, 0.2, 10.0),
            "claude-haiku-4-5" => (1.0, 1.25, 0.1, 5.0),
            "claude-haiku-5-5" => (0.10, 0.125, 0.01, 0.50),
            _ => throw new ArgumentException($"No price recorded for {model}.", nameof(model)),
        };

        return ((usage.InputTokens * input) + (usage.CacheWriteTokens * write) + (usage.CacheReadTokens * read)
            + (usage.OutputTokens * output)) / 1_000_000;
    }
}
