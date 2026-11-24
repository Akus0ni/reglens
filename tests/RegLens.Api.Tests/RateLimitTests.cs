using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using ModelContextProtocol.Protocol;

namespace RegLens.Api.Tests;

/// <summary>Per-caller limits shared by REST and MCP (spec 0006, D59, R6.11, P4).</summary>
public sealed class RateLimitTests : IDisposable
{
    private readonly McpTestApi _api = new(builder =>
    {
        builder.UseSetting("RateLimits:AskPerMinute", "2");
        builder.UseSetting("RateLimits:ReadPerMinute", "1");
    });

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task AnswersTooManyAsksWith429AndRetryAfter()
    {
        (await Ask()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Ask()).StatusCode.Should().Be(HttpStatusCode.OK);

        var third = await Ask();

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        int.Parse(third.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture).Should().BeInRange(1, 60);
        (await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Rate limit exceeded");
    }

    [Fact]
    public async Task SharesTheAskBucketBetweenRestAndMcp()
    {
        (await Ask()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Ask()).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var client = await _api.ConnectAsync();

        var result = await client.CallToolAsync(
            "ask_regulations", new Dictionary<string, object?> { ["question"] = "kyc" }, cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().StartWith("Rate limit exceeded. Retry after ");
    }

    [Fact]
    public async Task CountsToolCallsButNotToolListings()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = await _api.ConnectAsync();
        await client.ListToolsAsync(cancellationToken: ct);
        await client.ListToolsAsync(cancellationToken: ct);

        var first = await client.CallToolAsync("get_passage", new Dictionary<string, object?> { ["chunkId"] = 42 }, cancellationToken: ct);
        var second = await client.CallToolAsync("search_regulations", new Dictionary<string, object?> { ["query"] = "kyc" }, cancellationToken: ct);

        first.IsError.Should().NotBe(true);
        second.IsError.Should().BeTrue("the read bucket allows 1 a minute and get_passage used it");
        using var http = TestApi.Client(_api.Factory);
        (await http.GetAsync(new Uri("/passages/42", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task<HttpResponseMessage> Ask()
    {
        using var client = TestApi.Client(_api.Factory);
        using var body = new StringContent("""{"question":"When must a breach be reported?"}""", Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri("/ask", UriKind.Relative), body, TestContext.Current.CancellationToken);
    }
}
