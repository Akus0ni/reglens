using Npgsql;
using RegLens.Application.Answers;

namespace RegLens.Infrastructure.Persistence;

/// <summary>Full passage text by chunk id (spec 0003, R3.2).</summary>
public sealed class PostgresPassageReader(NpgsqlDataSource dataSource) : IPassageReader
{
    public async Task<IReadOnlyList<StoredPassage>> GetAsync(IReadOnlyList<long> chunkIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        if (chunkIds.Count == 0)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand("""
            SELECT c.id, c.document_id, d.title, c.para_ref, c.heading, c.page_start, c.page_end, c.content
            FROM chunks c JOIN documents d ON d.id = c.document_id
            WHERE c.id = ANY(@ids)
            """);
        command.Parameters.AddWithValue("ids", chunkIds.ToArray());

        var byId = new Dictionary<long, StoredPassage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var passage = new StoredPassage(
                ChunkId: reader.GetInt64(0),
                DocumentId: reader.GetString(1),
                DocumentTitle: reader.GetString(2),
                ParaRef: reader.GetString(3),
                Heading: reader.GetString(4),
                PageStart: reader.GetInt32(5),
                PageEnd: reader.GetInt32(6),
                Content: reader.GetString(7));
            byId[passage.ChunkId] = passage;
        }

        return [.. chunkIds.Where(byId.ContainsKey).Distinct().Select(id => byId[id])];
    }
}
