using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Api.Tests;

/// <summary><c>GET /search</c> with the database and Ollama replaced by substitutes (spec 0002, R2.12–R2.13).</summary>
public sealed class SearchEndpointTests : IDisposable
{
    private readonly IChunkSearch _search = Substitute.For<IChunkSearch>();
    private readonly WebApplicationFactory<Program> _factory;

    public SearchEndpointTests()
    {
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]));
        _search.HybridAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([new SearchHit(42, "dpdp-rules-2025", "Digital Personal Data Protection Rules, 2025", "Rule 7",
                "", 26, 26, "7. Intimation of personal data breach.", 1, 1, 0.0328)]);
        _search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _factory = TestApi.Create(services =>
            {
                services.RemoveAll<IChunkSearch>().AddSingleton(_search);
                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>().AddSingleton(generator);
            });
    }

    [Fact]
    public async Task ReturnsHitsWithRanksAsCamelCaseJson()
    {
        var response = await Get("/search?q=data%20breach");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var hit = json.RootElement.GetProperty("hits")[0];
        json.RootElement.GetProperty("query").GetString().Should().Be("data breach");
        hit.GetProperty("paraRef").GetString().Should().Be("Rule 7");
        hit.GetProperty("denseRank").GetInt32().Should().Be(1);
        hit.GetProperty("keywordRank").GetInt32().Should().Be(1);
        hit.GetProperty("fusedScore").GetDouble().Should().Be(0.0328);
        hit.GetProperty("pinned").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("/search", "q")]
    [InlineData("/search?q=%20%20", "q")]
    [InlineData("/search?q=kyc&topK=0", "topK")]
    [InlineData("/search?q=kyc&topK=21", "topK")]
    public async Task RejectsInvalidInputWithProblemDetails(string url, string field)
    {
        var response = await Get(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task RejectsAQueryLongerThan500Characters()
    {
        var response = await Get("/search?q=" + new string('x', 501));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectsATopKThatIsNotANumber()
    {
        var response = await Get("/search?q=kyc&topK=abc");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpResponseMessage> Get(string url)
    {
        using var client = TestApi.Client(_factory);
        return await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
    }
}
