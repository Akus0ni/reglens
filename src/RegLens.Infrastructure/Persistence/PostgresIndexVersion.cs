using Npgsql;
using RegLens.Application.Answers;

namespace RegLens.Infrastructure.Persistence;

/// <summary>
/// md5 over every document's id, file hash and ingest time (spec 0003, P8, R3.13). Ingestion rewrites a document's
/// row whenever it replaces that document's chunks, so any re-ingest changes the version; skipped documents do not.
/// </summary>
public sealed class PostgresIndexVersion(NpgsqlDataSource dataSource) : IIndexVersion
{
    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT coalesce(md5(string_agg(id || ':' || sha256 || ':' || extract(epoch FROM ingested_at)::text, ',' ORDER BY id)), 'empty')
            FROM documents
            """);
        return (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}
