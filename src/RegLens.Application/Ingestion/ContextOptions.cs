using System.ComponentModel.DataAnnotations;

namespace RegLens.Application.Ingestion;

/// <summary>Contextual retrieval during ingestion (spec 0005, D50, P3, P4, P8). Section "Context".</summary>
public sealed class ContextOptions
{
    public const string SectionName = "Context";

    /// <summary>False: no Anthropic call, null contexts, pipeline "no context" (P8, the D53 rebuild).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The chunk-context model (D50).</summary>
    [Required]
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Low, Medium or High, sent as <c>output_config.effort</c> (P3).</summary>
    [RegularExpression("^(Low|Medium|High)$")]
    public string Effort { get; set; } = "Low";

    /// <summary>Bounds thinking plus the context (P3); thinking tokens count toward it.</summary>
    [Range(256, 8192)]
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Parallel calls after a document's first chunk has written the prompt cache (P4).</summary>
    [Range(1, 16)]
    public int Concurrency { get; set; } = 4;

    [Range(5, 600)]
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>The SDK's own retries; no other retry layer wraps the call.</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;
}
