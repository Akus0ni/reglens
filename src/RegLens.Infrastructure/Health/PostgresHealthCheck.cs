using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace RegLens.Infrastructure.Health;

/// <summary>Readiness: Postgres answers <c>SELECT 1</c> (spec 0006, D61, R6.12). Unhealthy without it.</summary>
public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Postgres is unreachable.", exception);
        }
    }
}
