using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using RegLens.Application.Answers;

namespace RegLens.Api.Tests;

/// <summary><c>GET /passages/{id}</c> (spec 0006, D63, R6.7).</summary>
public sealed class PassageEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public PassageEndpointTests()
    {
        var passages = Substitute.For<IPassageReader>();
        passages.GetAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns([]);
        passages.GetAsync(Arg.Is<IReadOnlyList<long>>(ids => ids.Count == 1 && ids[0] == 42), Arg.Any<CancellationToken>())
            .Returns([new StoredPassage(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", "Rules", 26, 26, "7. Intimation of breach.")]);
        _factory = TestApi.Create(services => services.RemoveAll<IPassageReader>().AddSingleton(passages));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ReturnsThePassage()
    {
        var response = await Get("/passages/42");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("chunkId").GetInt64().Should().Be(42);
        json.RootElement.GetProperty("paraRef").GetString().Should().Be("Rule 7");
        json.RootElement.GetProperty("heading").GetString().Should().Be("Rules");
        json.RootElement.GetProperty("content").GetString().Should().Be("7. Intimation of breach.");
        json.RootElement.TryGetProperty("context", out _).Should().BeFalse("contexts serve retrieval only (D52)");
    }

    [Fact]
    public async Task ReturnsNotFoundForAnUnknownId()
    {
        var response = await Get("/passages/7");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("No passage with id 7.");
    }

    [Theory]
    [InlineData("/passages/0")]
    [InlineData("/passages/-3")]
    public async Task RejectsAnIdBelowOne(string url)
    {
        var response = await Get(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("chunkId");
    }

    private async Task<HttpResponseMessage> Get(string url)
    {
        using var client = TestApi.Client(_factory);
        return await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
    }
}
