using System.ComponentModel.DataAnnotations;

namespace RegLens.Infrastructure.Embeddings;

/// <summary>Local Ollama settings (D4). Section "Ollama".</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    [Required]
    public Uri Endpoint { get; set; } = new("http://localhost:11434");

    [Required]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Per-attempt timeout. The first call after Ollama unloads the model includes loading it
    /// (measured at 32 s on the owner's machine), so the standard 10 s would fail every cold start.
    /// </summary>
    [Range(5, 600)]
    public int AttemptTimeoutSeconds { get; set; } = 60;

    /// <summary>Timeout for one call including all retries.</summary>
    [Range(10, 1800)]
    public int TotalTimeoutSeconds { get; set; } = 180;
}
