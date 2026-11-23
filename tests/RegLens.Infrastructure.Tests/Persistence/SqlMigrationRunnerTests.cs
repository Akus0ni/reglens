using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

[Collection(PostgresTests.Name)]
public class SqlMigrationRunnerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AppliesEachMigrationOnceAndRecordsIt()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        var runner = new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance);

        var first = await runner.ApplyAsync(TestContext.Current.CancellationToken);
        var second = await runner.ApplyAsync(TestContext.Current.CancellationToken);

        first.Should().BeGreaterThan(0);
        second.Should().Be(0);
        (await Scalar<long>(dataSource, "SELECT count(*) FROM schema_version")).Should().Be(first);
    }

    [Fact]
    public async Task CreatesTheVectorExtensionTablesAndIndexes()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(TestContext.Current.CancellationToken);

        (await Scalar<long>(dataSource, "SELECT count(*) FROM pg_extension WHERE extname = 'vector'")).Should().Be(1);
        (await Scalar<long>(dataSource, """
            SELECT count(*) FROM pg_indexes
            WHERE tablename = 'chunks' AND indexname IN ('chunks_embedding_hnsw', 'chunks_tsv_gin', 'chunks_document_id')
            """)).Should().Be(3);
    }

    private static async Task<T> Scalar<T>(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
