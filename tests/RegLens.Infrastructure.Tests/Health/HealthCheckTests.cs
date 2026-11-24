using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OllamaSharp;
using OllamaSharp.Models;
using RegLens.Infrastructure.Embeddings;
using RegLens.Infrastructure.Health;

namespace RegLens.Infrastructure.Tests.Health;

/// <summary>The readiness checks (spec 0006, D61, R6.12): each reports its registration's failure status.</summary>
[Collection(PostgresTests.Name)]
public class HealthCheckTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PostgresIsHealthyWhenItAnswers()
    {
        var check = new PostgresHealthCheck(await postgres.CreateDatabaseAsync());

        (await Run(check, HealthStatus.Unhealthy)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task PostgresIsUnhealthyWhenUnreachable()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Timeout=1");

        var result = await Run(new PostgresHealthCheck(dataSource), HealthStatus.Unhealthy);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Postgres is unreachable.");
    }

    [Theory]
    [InlineData("nomic-embed-text:latest", "nomic-embed-text", true)]
    [InlineData("nomic-embed-text:v1.5", "nomic-embed-text:v1.5", true)]
    [InlineData("nomic-embed-text:latest", "nomic-embed-text:v1.5", false)]
    [InlineData("nomic-embed-text-v2:latest", "nomic-embed-text", false)]
    [InlineData("llama3:latest", "nomic-embed-text", false)]
    public async Task OllamaIsHealthyOnlyWithTheEmbeddingModel(string listed, string configured, bool healthy)
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ListLocalModelsAsync(Arg.Any<CancellationToken>()).Returns([new Model { Name = listed }]);
        var check = new OllamaHealthCheck(ollama, Options.Create(new OllamaOptions { EmbeddingModel = configured }));

        var result = await Run(check, HealthStatus.Unhealthy);

        result.Status.Should().Be(healthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);
        if (!healthy)
        {
            result.Description.Should().Be($"Ollama has no model '{configured}'. Run: ollama pull {configured}");
        }
    }

    [Fact]
    public async Task OllamaIsUnhealthyWhenUnreachable()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ListLocalModelsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("refused"));
        var check = new OllamaHealthCheck(ollama, Options.Create(new OllamaOptions()));

        var result = await Run(check, HealthStatus.Unhealthy);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Ollama is unreachable.");
    }

    [Fact]
    public async Task RedisIsDegradedWhenUnreachable()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("No connection"));

        var result = await Run(new RedisHealthCheck([cache]), HealthStatus.Degraded);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Redis is unreachable.");
    }

    [Fact]
    public async Task RedisIsHealthyWhenItAnswersOrIsNotConfigured()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        (await Run(new RedisHealthCheck([cache]), HealthStatus.Degraded)).Status.Should().Be(HealthStatus.Healthy);
        (await Run(new RedisHealthCheck([]), HealthStatus.Degraded)).Status.Should().Be(HealthStatus.Healthy);
    }

    private static Task<HealthCheckResult> Run(IHealthCheck check, HealthStatus failureStatus) =>
        check.CheckHealthAsync(
            new HealthCheckContext { Registration = new HealthCheckRegistration("check", check, failureStatus, null) },
            TestContext.Current.CancellationToken);
}
