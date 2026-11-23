using Npgsql;
using Pgvector;
using RegLens.Application.Ingestion;
using RegLens.Domain.Corpus;

namespace RegLens.Infrastructure.Persistence;

/// <summary>
/// Stores documents and chunks in Postgres with pgvector (spec 0001, R1.21–R1.22).
/// A document is replaced in one transaction, so a failure or Ctrl+C leaves the previous version intact (R1.25).
/// </summary>
public sealed class PostgresChunkStore(NpgsqlDataSource dataSource) : IChunkStore
{
    public async Task<StoredDocumentState> GetStateAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using var command = dataSource.CreateCommand("SELECT sha256 FROM documents WHERE id = $1");
        command.Parameters.AddWithValue(document.Id);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) switch
        {
            string stored when string.Equals(stored, document.Sha256, StringComparison.OrdinalIgnoreCase) => StoredDocumentState.Current,
            string => StoredDocumentState.Outdated,
            _ => StoredDocumentState.Missing,
        };
    }

    public async Task ReplaceDocumentAsync(
        SourceDocument document, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertDocumentAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false);
        await DeleteChunksAsync(connection, transaction, document.Id, cancellationToken).ConfigureAwait(false);
        await InsertChunksAsync(connection, transaction, chunks, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertDocumentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, SourceDocument document, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO documents (id, title, reference, version, source_url, sha256, ingested_at)
            VALUES ($1, $2, $3, $4, $5, $6, now())
            ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title, reference = EXCLUDED.reference, version = EXCLUDED.version,
                source_url = EXCLUDED.source_url, sha256 = EXCLUDED.sha256, ingested_at = EXCLUDED.ingested_at
            """, connection, transaction);
        command.Parameters.AddWithValue(document.Id);
        command.Parameters.AddWithValue(document.Title);
        command.Parameters.AddWithValue(document.Reference);
        command.Parameters.AddWithValue(document.Version);
        command.Parameters.AddWithValue(document.SourceUrl);
        command.Parameters.AddWithValue(document.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteChunksAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string documentId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("DELETE FROM chunks WHERE document_id = $1", connection, transaction);
        command.Parameters.AddWithValue(documentId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertChunksAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var (chunk, vector) in chunks)
        {
            var command = new NpgsqlBatchCommand("""
                INSERT INTO chunks (document_id, para_ref, heading, page_start, page_end, content, content_hash, token_estimate, embedding)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                ON CONFLICT (content_hash) DO NOTHING
                """);
            command.Parameters.AddWithValue(chunk.DocumentId);
            command.Parameters.AddWithValue(chunk.ParaRef);
            command.Parameters.AddWithValue(chunk.Heading);
            command.Parameters.AddWithValue(chunk.PageStart);
            command.Parameters.AddWithValue(chunk.PageEnd);
            command.Parameters.AddWithValue(chunk.Content);
            command.Parameters.AddWithValue(chunk.Hash.Value);
            command.Parameters.AddWithValue(chunk.TokenEstimate);
            command.Parameters.AddWithValue(new Vector(vector));
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshSearchStatisticsAsync(CancellationToken cancellationToken)
    {
        // CONCURRENTLY keeps the views readable by the API while they refresh; it needs their unique indexes.
        // chunk_lengths before corpus_stats, which is computed from it.
        await using var command = dataSource.CreateCommand("""
            REFRESH MATERIALIZED VIEW CONCURRENTLY lexeme_stats;
            REFRESH MATERIALIZED VIEW CONCURRENTLY chunk_lengths;
            REFRESH MATERIALIZED VIEW CONCURRENTLY corpus_stats;
            """);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
