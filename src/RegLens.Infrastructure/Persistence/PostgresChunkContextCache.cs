using Npgsql;
using RegLens.Application.Ingestion;

namespace RegLens.Infrastructure.Persistence;

/// <summary>The <c>chunk_contexts</c> table (spec 0005, P5, R5.7): generated contexts by input hash.</summary>
public sealed class PostgresChunkContextCache(NpgsqlDataSource dataSource) : IChunkContextCache
{
    public async Task<IReadOnlyDictionary<string, string>> GetAsync(
        IReadOnlyCollection<string> inputHashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputHashes);

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (inputHashes.Count == 0)
        {
            return found;
        }

        await using var command = dataSource.CreateCommand(
            "SELECT input_hash, context FROM chunk_contexts WHERE input_hash = ANY($1)");
        command.Parameters.AddWithValue(inputHashes.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            found[reader.GetString(0)] = reader.GetString(1);
        }

        return found;
    }

    public async Task SaveAsync(CachedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using var command = dataSource.CreateCommand("""
            INSERT INTO chunk_contexts
                (input_hash, model, prompt_version, context, input_tokens, cache_write_tokens, cache_read_tokens, output_tokens)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (input_hash) DO NOTHING
            """);
        command.Parameters.AddWithValue(context.InputHash);
        command.Parameters.AddWithValue(context.Model);
        command.Parameters.AddWithValue(context.PromptVersion);
        command.Parameters.AddWithValue(context.Context);
        command.Parameters.AddWithValue(checked((int)context.Usage.InputTokens));
        command.Parameters.AddWithValue(checked((int)context.Usage.CacheWriteTokens));
        command.Parameters.AddWithValue(checked((int)context.Usage.CacheReadTokens));
        command.Parameters.AddWithValue(checked((int)context.Usage.OutputTokens));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
