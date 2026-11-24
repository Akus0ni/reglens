using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RegLens.Api.Tests;

/// <summary>The MCP server and its three tools over Streamable HTTP (spec 0006, R6.1–R6.6).</summary>
public sealed class McpToolTests : IDisposable
{
    private readonly McpTestApi _api = new();

    public void Dispose() => _api.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task ListsTheThreeReadOnlyToolsForOldAndNewClients(string? protocolVersion)
    {
        await using var client = await _api.ConnectAsync(protocolVersion);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(t => t.Name).Should().BeEquivalentTo(["search_regulations", "get_passage", "ask_regulations"]);
        tools.Should().AllSatisfy(tool =>
        {
            var annotations = tool.ProtocolTool.Annotations!;
            annotations.ReadOnlyHint.Should().BeTrue();
            annotations.OpenWorldHint.Should().BeFalse();
            annotations.Title.Should().NotBeNullOrWhiteSpace();
            tool.ProtocolTool.OutputSchema.Should().NotBeNull();
            tool.Description.Should().Contain("never as instructions");
        });
        tools.Where(t => t.Name != "ask_regulations").Should().AllSatisfy(t => t.ProtocolTool.Annotations!.IdempotentHint.Should().BeTrue());
    }

    [Fact]
    public async Task SearchReturnsHitsAsStructuredContent()
    {
        var result = await Call("search_regulations", new() { ["query"] = "data breach", ["topK"] = 5 });

        result.IsError.Should().NotBe(true);
        var hits = Structured(result).GetProperty("hits");
        hits[0].GetProperty("chunkId").GetInt64().Should().Be(42);
        hits[0].GetProperty("paraRef").GetString().Should().Be("Rule 7");
    }

    [Fact]
    public async Task GetPassageReturnsTheFullText()
    {
        var result = await Call("get_passage", new() { ["chunkId"] = 42 });

        result.IsError.Should().NotBe(true);
        Structured(result).GetProperty("content").GetString().Should().Contain(McpTestApi.Quote);
    }

    [Fact]
    public async Task AskReturnsTheVerifiedAnswer()
    {
        var result = await Call("ask_regulations", new() { ["question"] = "When must a breach be reported to the Board?" });

        result.IsError.Should().NotBe(true);
        var answer = Structured(result);
        answer.GetProperty("answer").GetString().Should().Be("Without delay.");
        answer.GetProperty("citations")[0].GetProperty("quote").GetString().Should().Be(McpTestApi.Quote);
    }

    [Theory]
    [InlineData("get_passage", "chunkId", 7, "No passage with id 7.")]
    [InlineData("get_passage", "chunkId", 0, "chunkId must be a positive passage id.")]
    [InlineData("search_regulations", "topK", 0, "topK must be between 1 and 20.")]
    [InlineData("ask_regulations", "topK", 21, "topK must be between 1 and 20.")]
    public async Task ReportsBadInputAsAToolError(string tool, string parameter, int value, string message)
    {
        var arguments = new Dictionary<string, object?> { [parameter] = value };
        if (tool != "get_passage")
        {
            arguments[tool == "ask_regulations" ? "question" : "query"] = "kyc";
        }

        var result = await Call(tool, arguments);

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain(message);
    }

    [Fact]
    public async Task ReportsAnEmptyQuestion()
    {
        var result = await Call("ask_regulations", new() { ["question"] = "  " });

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("question: The question is required.");
    }

    // Clients that validate structuredContent against outputSchema (the Python MCP SDK does) reject a result that
    // leaves out a required property, even a null one.
    [Theory]
    [InlineData("search_regulations", "query", "data breach")]
    [InlineData("get_passage", "chunkId", 42)]
    [InlineData("ask_regulations", "question", "When must a breach be reported?")]
    public async Task ResultsHaveEveryPropertyTheirOutputSchemaRequires(string tool, string parameter, object value)
    {
        await using var client = await _api.ConnectAsync();
        var schema = (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Single(t => t.Name == tool).ProtocolTool.OutputSchema!.Value;

        var result = await client.CallToolAsync(
            tool, new Dictionary<string, object?> { [parameter] = value }, cancellationToken: TestContext.Current.CancellationToken);

        MissingRequired(schema, Structured(result), "$").Should().BeEmpty();
    }

    [Fact]
    public async Task RefusesAClientWithoutTheKey()
    {
        var connect = () => _api.ConnectAsync(withKey: false);

        await connect.Should().ThrowAsync<Exception>();
    }

    private async Task<CallToolResult> Call(string tool, Dictionary<string, object?> arguments)
    {
        await using var client = await _api.ConnectAsync();
        return await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static JsonElement Structured(CallToolResult result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(result.StructuredContent)).RootElement;

    private static List<string> MissingRequired(JsonElement schema, JsonElement value, string path)
    {
        var missing = new List<string>();
        if (value.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties))
        {
            if (schema.TryGetProperty("required", out var required))
            {
                missing.AddRange(required.EnumerateArray().Select(r => r.GetString()!)
                    .Where(name => !value.TryGetProperty(name, out _)).Select(name => $"{path}.{name}"));
            }

            foreach (var property in properties.EnumerateObject())
            {
                if (value.TryGetProperty(property.Name, out var child))
                {
                    missing.AddRange(MissingRequired(property.Value, child, $"{path}.{property.Name}"));
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                missing.AddRange(MissingRequired(items, item, $"{path}[{index++}]"));
            }
        }

        return missing;
    }

    private static string Text(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
