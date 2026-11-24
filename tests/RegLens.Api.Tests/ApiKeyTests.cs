using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Api.Tests;

/// <summary>The API key (spec 0006, D58, R6.8–R6.10).</summary>
public sealed class ApiKeyTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiKeyTests()
    {
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]));
        var search = Substitute.For<IChunkSearch>();
        search.HybridAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        _factory = TestApi.Create(services =>
        {
            services.RemoveAll<IChunkSearch>().AddSingleton(search);
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>().AddSingleton(generator);
        });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task AcceptsTheKey()
    {
        using var client = TestApi.Client(_factory);

        var response = await client.GetAsync(new Uri("/search?q=kyc", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData("test-key-0123456789abcdef0123456789abcdeX")]
    public async Task RejectsAMissingOrWrongKeyOnEveryProtectedEndpoint(string? key)
    {
        using var client = _factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, key);
        }

        var ct = TestContext.Current.CancellationToken;
        (await client.GetAsync(new Uri("/search?q=kyc", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync(new Uri("/passages/1", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var ask = new StringContent("""{"question":"kyc"}""", System.Text.Encoding.UTF8, "application/json");
        (await client.PostAsync(new Uri("/ask", UriKind.Relative), ask, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var mcp = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json");
        (await client.PostAsync(new Uri("/mcp", UriKind.Relative), mcp, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LeavesLivenessOpen()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("", "is not set")]
    [InlineData("short-key", "at least 32 characters")]
    public void RefusesToStartWithoutAUsableKey(string key, string message)
    {
        using var factory = TestApi.Create(_ => { }, builder => builder.UseSetting("Auth:ApiKey", key));

        var start = () => factory.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage($"*{message}*Auth__ApiKey*");
    }
}
