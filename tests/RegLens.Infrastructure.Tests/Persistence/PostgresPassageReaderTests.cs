using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

/// <summary>Full passages by id and the index version (spec 0003, R3.2, R3.13).</summary>
[Collection(PostgresTests.Name)]
public class PostgresPassageReaderTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReturnsFullTextInTheRequestedOrderAndSkipsUnknownIds()
    {
        var dataSource = await Migrated();
        await Store(dataSource, Document("doc", 'a'), ("Para 1", new string('x', 400)), ("Para 2", "2. Short."));
        var ids = await ChunkIds(dataSource);

        var passages = await new PostgresPassageReader(dataSource).GetAsync([ids[1], 999_999, ids[0]], Ct);

        passages.Select(p => p.ParaRef).Should().Equal("Para 2", "Para 1");
        passages[1].Content.Should().HaveLength(400, "the full text, not the 300-character excerpt");
        passages[0].DocumentTitle.Should().Be("Test Directions");
        passages[0].Heading.Should().Be("Heading", "prompt v3 sends it (spec 0005, R5.14)");
        (await new PostgresPassageReader(dataSource).GetAsync([], Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangesTheIndexVersionWhenADocumentIsReplaced()
    {
        var dataSource = await Migrated();
        var version = new PostgresIndexVersion(dataSource);
        var empty = await version.GetAsync(Ct);

        await Store(dataSource, Document("doc", 'a'), ("Para 1", "1. First."));
        var first = await version.GetAsync(Ct);
        var unchanged = await version.GetAsync(Ct);
        await Store(dataSource, Document("doc", 'b'), ("Para 1", "1. First, amended."));
        var second = await version.GetAsync(Ct);

        empty.Should().Be("empty");
        first.Should().NotBe(empty).And.Be(unchanged);
        second.Should().NotBe(first);
    }

    private async Task<NpgsqlDataSource> Migrated()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(Ct);
        return dataSource;
    }

    private static SourceDocument Document(string id, char hash) => new(id, $"{id}.pdf", "Test Directions", "RBI/TEST", "v1",
        "https://example.test", new string(hash, 64), 100, 1, new PageRange(1, 1), DocumentStructure.RbiDirections);

    private static Task Store(NpgsqlDataSource dataSource, SourceDocument document, params (string ParaRef, string Content)[] chunks) =>
        new PostgresChunkStore(dataSource).ReplaceDocumentAsync(document, "test",
            [.. chunks.Select((c, i) => new EmbeddedChunk(new Chunk(document.Id, c.ParaRef, "Heading", 1, 1, c.Content), Axis(i)))],
            Ct);

    private static async Task<List<long>> ChunkIds(NpgsqlDataSource dataSource)
    {
        var ids = new List<long>();
        await using var command = dataSource.CreateCommand("SELECT id FROM chunks ORDER BY para_ref");
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static float[] Axis(int i)
    {
        var v = new float[768];
        v[i] = 1;
        return v;
    }
}
