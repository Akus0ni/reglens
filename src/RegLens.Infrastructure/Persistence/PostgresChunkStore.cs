using Npgsql;
using Pgvector;
using RegLens.Application.Ingestion;
using RegLens.Domain.Corpus;

namespace RegLens.Infrastructure.Persistence;

/// <summary>
/// Stores documents and chunks in Postgres with pgvector (spec 0001, R1.21–R1.22).
/// A document is replaced in one transaction, so a failure or Ctrl+C leaves the previous version intact (R1.25).
/// The document row records the pipeline that built its chunks (spec 0005, P6).
/// </summary>
public sealed class PostgresChunkStore(NpgsqlDataSource dataSource) : IChunkStore
{
    public async Task<StoredDocumentState> GetStateAsync(SourceDocument document, string pipeline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pipeline);

        await using var command = dataSource.CreateCommand("SELECT sha256, pipeline FROM documents WHERE id = $1");
        command.Parameters.AddWithValue(document.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return StoredDocumentState.Missing;
        }

        var sameFile = string.Equals(reader.GetString(0), document.Sha256, StringComparison.OrdinalIgnoreCase);
        var samePipeline = string.Equals(reader.GetString(1), pipeline, StringComparison.Ordinal);
        return sameFile && samePipeline ? StoredDocumentState.Current : StoredDocumentState.Outdated;
    }

    public async Task ReplaceDocumentAsync(
        SourceDocument document, string pipeline, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(chunks);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertDocumentAsync(connection, transaction, document, pipeline, cancellationToken).ConfigureAwait(false);
        await DeleteChunksAsync(connection, transaction, document.Id, cancellationToken).ConfigureAwait(false);
        await InsertChunksAsync(connection, transaction, chunks, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SourceDocument document,
        string pipeline,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO documents (id, title, reference, version, source_url, sha256, pipeline, ingested_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now())
            ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title, reference = EXCLUDED.reference, version = EXCLUDED.version,
                source_url = EXCLUDED.source_url, sha256 = EXCLUDED.sha256, pipeline = EXCLUDED.pipeline,
                ingested_at = EXCLUDED.ingested_at
            """, connection, transaction);
        command.Parameters.AddWithValue(document.Id);
        command.Parameters.AddWithValue(document.Title);
        command.Parameters.AddWithValue(document.Reference);
        command.Parameters.AddWithValue(document.Version);
        command.Parameters.AddWithValue(document.SourceUrl);
        command.Parameters.AddWithValue(document.Sha256);
        command.Parameters.AddWithValue(pipeline);
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
        foreach (var (chunk, vector, context) in chunks)
        {
            var command = new NpgsqlBatchCommand("""
                INSERT INTO chunks
                    (document_id, para_ref, heading, page_start, page_end, content, content_hash, token_estimate, embedding, context)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
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
            command.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = context, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
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
