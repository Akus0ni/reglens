using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

/// <summary>
/// The API's SELECT-only login (spec 0006, D60, R6.16, R6.18): a login in <c>reglens_readonly</c> reads every table and
/// BM25 view, and every write is refused.
/// </summary>
[Collection(PostgresTests.Name)]
public class ReadOnlyRoleTests(PostgresFixture postgres)
{
    private const string Password = "read-only-test";
    private const string InsufficientPrivilege = "42501";

    public static TheoryData<string> Writes =>
    [
        "INSERT INTO documents (id, title, reference, version, source_url, sha256, pipeline) VALUES ('x', 'x', 'x', 'x', 'x', 'x', 'x')",
        "UPDATE chunks SET heading = 'x'",
        "DELETE FROM chunks",
        "DELETE FROM chunk_contexts",
        "TRUNCATE schema_version",
        "REFRESH MATERIALIZED VIEW corpus_stats",
        "CREATE TABLE intruder (id int)",
    ];

    [Fact]
    public async Task ReadsEveryTableAndBm25View()
    {
        await using var login = await ReadOnlyLoginAsync();

        foreach (var relation in new[] { "documents", "chunks", "chunk_contexts", "schema_version", "lexeme_stats", "chunk_lengths", "corpus_stats" })
        {
            await using var command = login.DataSource.CreateCommand($"SELECT count(*) FROM {relation}");
            var read = async () => await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            await read.Should().NotThrowAsync($"reglens_readonly reads {relation}");
        }
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task RefusesEveryWrite(string sql)
    {
        await using var login = await ReadOnlyLoginAsync();
        await using var command = login.DataSource.CreateCommand(sql);

        var write = async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        (await write.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(InsufficientPrivilege);
    }

    [Fact]
    public async Task ReadsTablesThatLaterMigrationsCreate()
    {
        await using var login = await ReadOnlyLoginAsync();
        await using (var create = login.Owner.CreateCommand("CREATE TABLE later_table (id int)"))
        {
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var command = login.DataSource.CreateCommand("SELECT count(*) FROM later_table");
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(0L);
    }

    [Fact]
    public async Task CreatesTheRoleOnlyOnceAcrossDatabases()
    {
        var first = await postgres.CreateDatabaseAsync();
        var second = await postgres.CreateDatabaseAsync();

        // Both apply migration 005; roles are cluster-wide, so the second finds the role already there.
        await new SqlMigrationRunner(first, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(TestContext.Current.CancellationToken);
        var apply = () => new SqlMigrationRunner(second, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(TestContext.Current.CancellationToken);

        await apply.Should().NotThrowAsync();
    }

    // A fresh migrated database and a login in reglens_readonly, as scripts/create-api-db-user.ps1 makes it.
    private async Task<ReadOnlyLogin> ReadOnlyLoginAsync()
    {
        var owner = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(owner, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(TestContext.Current.CancellationToken);
        var name = $"api_{Guid.NewGuid():N}";
        await Execute(owner, $"CREATE ROLE {name} LOGIN PASSWORD '{Password}' IN ROLE reglens_readonly");

        var connection = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username = name, Password = Password, Pooling = false };
        return new ReadOnlyLogin(owner, NpgsqlDataSource.Create(connection.ConnectionString), name);
    }

    private static async Task Execute(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed record ReadOnlyLogin(NpgsqlDataSource Owner, NpgsqlDataSource DataSource, string Name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            await Execute(Owner, $"DROP ROLE {Name}");
        }
    }
}
