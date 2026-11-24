using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RegLens.Application.Telemetry;

namespace RegLens.Infrastructure.Persistence;

/// <summary>
/// Applies the embedded <c>Migrations/NNN_name.sql</c> scripts in order, each in its own transaction,
/// and records them in <c>schema_version</c> (spec 0001, R1.7–R1.9, D24).
/// </summary>
public sealed partial class SqlMigrationRunner(NpgsqlDataSource dataSource, ILogger<SqlMigrationRunner> logger)
{
    private const string ResourcePrefix = "RegLens.Infrastructure.Migrations.";

    // Any constant works; it only stops two runners from migrating at the same moment.
    private const long MigrationLockKey = 0x5265674C656E73;

    /// <summary>Applies pending migrations and returns how many were applied.</summary>
    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        using var activity = RegLensActivities.Source.StartActivity("migrate");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS schema_version (
                version    int         PRIMARY KEY,
                name       text        NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT now()
            )
            """, cancellationToken).ConfigureAwait(false);

        var applied = 0;
        foreach (var migration in Migrations())
        {
            if (await TryApplyAsync(connection, migration, cancellationToken).ConfigureAwait(false))
            {
                applied++;
                LogApplied(migration.Name);
            }
        }

        if (applied > 0)
        {
            // The first migration creates the pgvector type after the data source has loaded its type map.
            await connection.ReloadTypesAsync(cancellationToken).ConfigureAwait(false);
        }

        return applied;
    }

    private static async Task<bool> TryApplyAsync(NpgsqlConnection connection, Migration migration, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, $"SELECT pg_advisory_xact_lock({MigrationLockKey})", cancellationToken)
            .ConfigureAwait(false);

        await using (var check = new NpgsqlCommand("SELECT 1 FROM schema_version WHERE version = $1", connection, transaction))
        {
            check.Parameters.AddWithValue(migration.Version);
            if (await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return false;
            }
        }

        try
        {
            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken).ConfigureAwait(false);
            await using var record = new NpgsqlCommand(
                "INSERT INTO schema_version (version, name) VALUES ($1, $2)", connection, transaction);
            record.Parameters.AddWithValue(migration.Version);
            record.Parameters.AddWithValue(migration.Name);
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException exception)
        {
            throw new InvalidOperationException(
                $"Migration {migration.Name} failed and was rolled back: {exception.MessageText}", exception);
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<Migration> Migrations()
    {
        var assembly = typeof(SqlMigrationRunner).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => Load(assembly, name))
            .OrderBy(migration => migration.Version);
    }

    private static Migration Load(Assembly assembly, string resourceName)
    {
        var name = resourceName[ResourcePrefix.Length..^".sql".Length];
        var prefix = name.Split('_', 2)[0];
        if (!int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidOperationException($"Migration '{name}.sql' must start with a number, like 001_init.sql.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration resource '{resourceName}' could not be read.");
        using var reader = new StreamReader(stream);
        return new Migration(version, name, reader.ReadToEnd());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Name}")]
    private partial void LogApplied(string name);

    private sealed record Migration(int Version, string Name, string Sql);
}
