using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RegLens.Api.Tests;

/// <summary><c>/health/live</c> and <c>/health/ready</c> (spec 0006, D61, R6.12), with each check's outcome fixed.</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task IsReadyWhenEveryCheckPasses()
    {
        var (status, body) = await Ready(postgres: true, ollama: true, redis: true);

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Healthy");
    }

    [Fact]
    public async Task IsDegradedButReadyWithoutRedis()
    {
        var (status, body) = await Ready(postgres: true, ollama: true, redis: false);

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Degraded");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task IsNotReadyWithoutPostgresOrOllama(bool postgres, bool ollama)
    {
        var (status, body) = await Ready(postgres, ollama, redis: true);

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task StaysLiveWhenNotReady()
    {
        using var factory = Api(postgres: false, ollama: false, redis: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<(HttpStatusCode Status, string Body)> Ready(bool postgres, bool ollama, bool redis)
    {
        using var factory = Api(postgres, ollama, redis);
        using var client = factory.CreateClient(); // no key: readiness is anonymous (R6.10)
        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // Keeps each registration's name, failure status and tags; only the check itself is replaced.
    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Api(bool postgres, bool ollama, bool redis) =>
        TestApi.Create(services => services.Configure<HealthCheckServiceOptions>(options =>
        {
            var passing = new Dictionary<string, bool>(StringComparer.Ordinal) { ["postgres"] = postgres, ["ollama"] = ollama, ["redis"] = redis };
            foreach (var registration in options.Registrations)
            {
                var passes = passing[registration.Name];
                registration.Factory = _ => new FixedCheck(passes);
            }
        }));

    private sealed class FixedCheck(bool passes) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(passes ? HealthCheckResult.Healthy() : new HealthCheckResult(context.Registration.FailureStatus));
    }
}
