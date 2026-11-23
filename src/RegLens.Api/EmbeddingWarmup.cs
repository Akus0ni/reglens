using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RegLens.Application.Search;

namespace RegLens.Api;

/// <summary>
/// Loads the embedding model in the background at startup (spec 0002, R2.14, P6), so the first search does not wait
/// ~30 s for Ollama to load it. Startup never waits for this, and a failure is only logged.
/// </summary>
public sealed partial class EmbeddingWarmup(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<SearchOptions> options,
    ILogger<EmbeddingWarmup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await generator.GenerateVectorAsync(options.Value.QueryPrefix + "warm-up", cancellationToken: stoppingToken)
                .ConfigureAwait(false);
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            LogWarm(seconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down before the model loaded.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedding model ready after {Seconds:F1}s")]
    private partial void LogWarm(double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedding warm-up failed; searches will fall back to keyword-only until Ollama answers")]
    private partial void LogFailed(Exception exception);
}
