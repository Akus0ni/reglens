using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

[Collection(PostgresTests.Name)]
public class PostgresChunkStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TracksWhetherTheStoredDocumentIsMissingCurrentOrOutdated()
    {
        var (store, _) = await NewStore();

        (await store.GetStateAsync(Document("a"), Ct)).Should().Be(StoredDocumentState.Missing);
        await store.ReplaceDocumentAsync(Document("a"), [Embedded("1. Text.")], Ct);
        (await store.GetStateAsync(Document("a"), Ct)).Should().Be(StoredDocumentState.Current);
        (await store.GetStateAsync(Document("b"), Ct)).Should().Be(StoredDocumentState.Outdated);
    }

    [Fact]
    public async Task StoresTheVectorAndGeneratesTheSearchColumn()
    {
        var (store, dataSource) = await NewStore();
        var chunk = Embedded("9. An NBFC shall provide a Key Fact Statement to the borrower.", seed: 0.25f);

        await store.ReplaceDocumentAsync(Document("a"), [chunk], Ct);

        await using var command = dataSource.CreateCommand(
            "SELECT embedding, tsv @@ websearch_to_tsquery('english', 'key fact statement') FROM chunks");
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        reader.GetFieldValue<Vector>(0).ToArray().Should().Equal(chunk.Vector.ToArray());
        reader.GetBoolean(1).Should().BeTrue();
    }

    [Fact]
    public async Task ReplacingADocumentRemovesItsOldChunks()
    {
        var (store, dataSource) = await NewStore();
        await store.ReplaceDocumentAsync(Document("a"), [Embedded("1. One."), Embedded("2. Two."), Embedded("3. Three.")], Ct);

        await store.ReplaceDocumentAsync(Document("b"), [Embedded("1. One, revised.")], Ct);

        (await Count(dataSource)).Should().Be(1);
    }

    [Fact]
    public async Task IgnoresADuplicateChunkInsteadOfFailing()
    {
        var (store, dataSource) = await NewStore();
        var chunk = Embedded("1. Repeated boilerplate.");

        await store.ReplaceDocumentAsync(Document("a"), [chunk, chunk], Ct);

        (await Count(dataSource)).Should().Be(1);
    }

    [Fact]
    public async Task AFailedReplaceLeavesThePreviousVersionIntact()
    {
        var (store, dataSource) = await NewStore();
        await store.ReplaceDocumentAsync(Document("a"), [Embedded("1. One."), Embedded("2. Two.")], Ct);
        var wrongDimensions = new EmbeddedChunk(Chunk("1. Broken."), new float[3]);

        var act = () => store.ReplaceDocumentAsync(Document("b"), [wrongDimensions], Ct);

        await act.Should().ThrowAsync<PostgresException>();
        (await Count(dataSource)).Should().Be(2);
        (await store.GetStateAsync(Document("a"), Ct)).Should().Be(StoredDocumentState.Current);
    }

    private async Task<(PostgresChunkStore Store, NpgsqlDataSource DataSource)> NewStore()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(Ct);
        return (new PostgresChunkStore(dataSource), dataSource);
    }

    private static async Task<long> Count(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM chunks");
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    // Two versions of the same document differ only by SHA-256 ("a" or "b" repeated).
    private static SourceDocument Document(string version) => new(
        "doc", "doc.pdf", "Test Directions", "RBI/TEST", version, "https://example.test", new string(version[0], 64),
        100, 1, new PageRange(1, 1), DocumentStructure.RbiDirections);

    private static Chunk Chunk(string content) => new("doc", content[..2], "Chapter I – Preliminary", 1, 1, content);

    private static EmbeddedChunk Embedded(string content, float seed = 0.5f) =>
        new(Chunk(content), Enumerable.Range(0, 768).Select(i => seed + (i * 0.001f)).ToArray());
}
