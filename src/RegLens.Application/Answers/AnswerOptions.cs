using System.ComponentModel.DataAnnotations;

namespace RegLens.Application.Answers;

/// <summary>Answer settings (spec 0003). Section "Answers".</summary>
public sealed class AnswerOptions
{
    public const string SectionName = "Answers";

    /// <summary>The answer model (D3).</summary>
    [Required]
    public string Model { get; set; } = "claude-sonnet-5-5";

    /// <summary>Low, Medium or High (D36). Sent as <c>output_config.effort</c>.</summary>
    [RegularExpression("^(Low|Medium|High)$")]
    public string Effort { get; set; } = "Medium";

    /// <summary>Bounds thinking plus the answer (P11). The SDK default of 1024 is too small once thinking is on.</summary>
    [Range(1024, 64000)]
    public int MaxOutputTokens { get; set; } = 8192;

    /// <summary>Below this best similarity, abstain without calling the model (D33). 0.60 from the golden set (spec 0004, D48).</summary>
    [Range(0.0, 1.0)]
    public double SimilarityFloor { get; set; } = 0.60;

    [Range(1, 24 * 30)]
    public int CacheHours { get; set; } = 24;

    [Range(1, 24 * 30)]
    public int LocalCacheHours { get; set; } = 1;

    /// <summary>Per-request timeout for the model call (P10); the SDK default is 10 minutes.</summary>
    [Range(5, 600)]
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>The SDK's own retries (P10); no other retry layer wraps the model call.</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;
}
