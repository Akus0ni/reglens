using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OllamaSharp;
using RegLens.Infrastructure.Embeddings;

namespace RegLens.Infrastructure.Health;

/// <summary>
/// Readiness: Ollama answers and has the embedding model pulled (spec 0006, D61, R6.12). Search embeds every query,
/// so without it the API can only answer keyword-only. Listing models does not load one.
/// </summary>
public sealed class OllamaHealthCheck(IOllamaApiClient ollama, IOptions<OllamaOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var model = options.Value.EmbeddingModel;
        try
        {
            var models = await ollama.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
            return models.Any(m => IsModel(m.Name, model))
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"Ollama has no model '{model}'. Run: ollama pull {model}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Ollama is unreachable.", exception);
        }
    }

    // "nomic-embed-text" is listed as "nomic-embed-text:latest".
    internal static bool IsModel(string? listed, string configured) =>
        listed is not null
        && (string.Equals(listed, configured, StringComparison.OrdinalIgnoreCase)
            || (!configured.Contains(':', StringComparison.Ordinal)
                && listed.StartsWith(configured + ":", StringComparison.OrdinalIgnoreCase)));
}
