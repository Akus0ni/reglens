using Microsoft.Extensions.Logging.Abstractions;
using RegLens.Application;
using RegLens.Application.Ingestion;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

[Collection(PostgresTests.Name)]
public class PostgresChunkContextCacheTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReturnsOnlyTheContextsItHas() // R5.7
    {
        var cache = await NewCache();
        await cache.SaveAsync(Context(Hash('a'), "First context."), Ct);
        await cache.SaveAsync(Context(Hash('b'), "Second context."), Ct);

        var found = await cache.GetAsync([Hash('a'), Hash('c')], Ct);

        found.Should().BeEquivalentTo(new Dictionary<string, string> { [Hash('a')] = "First context." });
    }

    [Fact]
    public async Task KeepsTheFirstContextSavedForAHash()
    {
        var cache = await NewCache();

        await cache.SaveAsync(Context(Hash('a'), "First context."), Ct);
        await cache.SaveAsync(Context(Hash('a'), "Second context."), Ct);

        (await cache.GetAsync([Hash('a')], Ct))[Hash('a')].Should().Be("First context.");
    }

    [Fact]
    public async Task AnEmptyLookupMakesNoQuery()
    {
        var cache = await NewCache();

        (await cache.GetAsync([], Ct)).Should().BeEmpty();
    }

    private async Task<PostgresChunkContextCache> NewCache()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(Ct);
        return new PostgresChunkContextCache(dataSource);
    }

    private static string Hash(char c) => new(c, 64);

    private static CachedContext Context(string hash, string text) =>
        new(hash, "claude-haiku-5-5", ContextPrompt.Version, text, new TokenUsage(500, 0, 20_000, 80));
}
