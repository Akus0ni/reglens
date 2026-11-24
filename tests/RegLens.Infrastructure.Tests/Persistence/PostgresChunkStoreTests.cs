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
    private const string Pipeline = "chunker 2; no context";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TracksWhetherTheStoredDocumentIsMissingCurrentOrOutdated()
    {
        var (store, _) = await NewStore();

        (await store.GetStateAsync(Document("a"), Pipeline, Ct)).Should().Be(StoredDocumentState.Missing);
        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [Embedded("1. Text.")], Ct);
        (await store.GetStateAsync(Document("a"), Pipeline, Ct)).Should().Be(StoredDocumentState.Current);
        (await store.GetStateAsync(Document("b"), Pipeline, Ct)).Should().Be(StoredDocumentState.Outdated);
    }

    [Fact]
    public async Task ADifferentPipelineMakesTheSameFileOutdated() // R5.10
    {
        var (store, _) = await NewStore();
        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [Embedded("1. Text.")], Ct);

        (await store.GetStateAsync(Document("a"), "chunker 2; context claude-haiku-5-5 v1", Ct))
            .Should().Be(StoredDocumentState.Outdated);
        (await store.GetStateAsync(Document("a"), Pipeline, Ct)).Should().Be(StoredDocumentState.Current);
    }

    [Fact]
    public async Task ADocumentStoredBeforeMigration004IsOutdated()
    {
        var (store, dataSource) = await NewStore();
        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [Embedded("1. Text.")], Ct);
        await using (var reset = dataSource.CreateCommand("UPDATE documents SET pipeline = DEFAULT"))
        {
            await reset.ExecuteNonQueryAsync(Ct);
        }

        (await store.GetStateAsync(Document("a"), Pipeline, Ct)).Should().Be(StoredDocumentState.Outdated);
    }

    [Fact]
    public async Task StoresTheContextAndIndexesItForKeywordSearch() // P7: tsv already covers context
    {
        var (store, dataSource) = await NewStore();
        var chunk = Embedded("1. The entity shall keep the records.") with { Context = "Lending service provider duties." };

        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [chunk, Embedded("2. No context.")], Ct);

        await using var command = dataSource.CreateCommand("""
            SELECT para_ref, context, tsv @@ websearch_to_tsquery('english', 'lending service provider')
            FROM chunks ORDER BY para_ref
            """);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        (reader.GetString(1), reader.GetBoolean(2)).Should().Be(("Lending service provider duties.", true));
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        reader.IsDBNull(1).Should().BeTrue();
    }

    [Fact]
    public async Task StoresTheVectorAndGeneratesTheSearchColumn()
    {
        var (store, dataSource) = await NewStore();
        var chunk = Embedded("9. An NBFC shall provide a Key Fact Statement to the borrower.", seed: 0.25f);

        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [chunk], Ct);

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
        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [Embedded("1. One."), Embedded("2. Two."), Embedded("3. Three.")], Ct);

        await store.ReplaceDocumentAsync(Document("b"), Pipeline, [Embedded("1. One, revised.")], Ct);

        (await Count(dataSource)).Should().Be(1);
    }

    [Fact]
    public async Task IgnoresADuplicateChunkInsteadOfFailing()
    {
        var (store, dataSource) = await NewStore();
        var chunk = Embedded("1. Repeated boilerplate.");

        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [chunk, chunk], Ct);

        (await Count(dataSource)).Should().Be(1);
    }

    [Fact]
    public async Task AFailedReplaceLeavesThePreviousVersionIntact()
    {
        var (store, dataSource) = await NewStore();
        await store.ReplaceDocumentAsync(Document("a"), Pipeline, [Embedded("1. One."), Embedded("2. Two.")], Ct);
        var wrongDimensions = new EmbeddedChunk(Chunk("1. Broken."), new float[3]);

        var act = () => store.ReplaceDocumentAsync(Document("b"), Pipeline, [wrongDimensions], Ct);

        await act.Should().ThrowAsync<PostgresException>();
        (await Count(dataSource)).Should().Be(2);
        (await store.GetStateAsync(Document("a"), Pipeline, Ct)).Should().Be(StoredDocumentState.Current);
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
