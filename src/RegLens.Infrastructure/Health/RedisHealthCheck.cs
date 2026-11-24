using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RegLens.Infrastructure.Health;

/// <summary>
/// Readiness: the answer cache's second level answers (spec 0006, D61, R6.12). Registered with Degraded as its
/// failure status: HybridCache keeps working from memory without Redis.
/// </summary>
public sealed class RedisHealthCheck(IEnumerable<IDistributedCache> caches) : IHealthCheck
{
    private const string ProbeKey = "reglens:health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cache = caches.LastOrDefault();
        if (cache is null)
        {
            return HealthCheckResult.Healthy("No second-level cache is configured.");
        }

        try
        {
            await cache.GetAsync(ProbeKey, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // StackExchange.Redis throws several unrelated types; any failure means "not ready".
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested || exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis is unreachable.", exception);
        }
    }
}
