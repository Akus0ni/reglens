using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using RegLens.Application.Answers;
using Testcontainers.Redis;

namespace RegLens.Infrastructure.Tests.Answers;

/// <summary>HybridCache with Redis as its second level, wired as the API wires it (spec 0003, R3.12, A7, A8).</summary>
public sealed class RedisHybridCacheTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.8.4-alpine").Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _redis.StartAsync();

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task AnotherProcessReadsTheAnswerBackFromRedis()
    {
        var answer = new AnswerResult("q", "a", false, null, "",
            [new AnswerCitation(42, "dpdp", "DPDP Rules, 2025", "Rule 7", 26, 26, "the Data Fiduciary shall intimate")],
            0, null, false, "claude-sonnet-5-5", new AnswerUsage(1, 2));

        await using (var first = Provider(_redis.GetConnectionString()))
        {
            await Cache(first).GetOrCreateAsync("answer:test", _ => ValueTask.FromResult(answer), cancellationToken: Ct);
        }

        // A new provider has an empty memory cache, like a restarted API.
        await using var second = Provider(_redis.GetConnectionString());
        var factoryRan = false;
        var read = await Cache(second).GetOrCreateAsync("answer:test", _ =>
        {
            factoryRan = true;
            return ValueTask.FromResult(answer with { Answer = "fresh" });
        }, cancellationToken: Ct);

        factoryRan.Should().BeFalse();
        read.Should().BeEquivalentTo(answer);
    }

    [Fact]
    public async Task StillAnswersWhenRedisIsUnreachable()
    {
        // Nothing listens on port 1.
        await using var provider = Provider("127.0.0.1:1");

        var value = await Cache(provider).GetOrCreateAsync("answer:down", _ => ValueTask.FromResult("from the factory"), cancellationToken: Ct);

        value.Should().Be("from the factory");
    }

    private static ServiceProvider Provider(string redis)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStackExchangeRedisCache(options => options.Configuration = redis);
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static HybridCache Cache(ServiceProvider provider) => provider.GetRequiredService<HybridCache>();
}
