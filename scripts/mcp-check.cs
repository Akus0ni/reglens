#:package ModelContextProtocol
#:property PublishAot=false

// Calls the running API's MCP server the way the Day 2 agent will (spec 0006, A2/A3).
//   dotnet run scripts/mcp-check.cs -- list                     tools, annotations and output schemas (free)
//   dotnet run scripts/mcp-check.cs -- search "<query>" [topK]   search_regulations (free)
//   dotnet run scripts/mcp-check.cs -- passage <chunkId>         get_passage (free)
//   dotnet run scripts/mcp-check.cs -- ask "<question>"          ask_regulations (may call Claude: about $0.01-0.02)
// The key comes from REGLENS_API_KEY; the endpoint from REGLENS_MCP_URL (default http://127.0.0.1:5180/mcp).

using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var key = Environment.GetEnvironmentVariable("REGLENS_API_KEY");
if (string.IsNullOrWhiteSpace(key) || args.Length == 0)
{
    Console.Error.WriteLine("Set REGLENS_API_KEY and pass: list | search <query> [topK] | passage <chunkId> | ask <question>");
    return 2;
}

var endpoint = new Uri(Environment.GetEnvironmentVariable("REGLENS_MCP_URL") ?? "http://127.0.0.1:5180/mcp");
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = endpoint,
    TransportMode = HttpTransportMode.StreamableHttp,
    AdditionalHeaders = new Dictionary<string, string> { ["X-API-Key"] = key },
});
await using var client = await McpClient.CreateAsync(transport);
Console.WriteLine($"Connected: {client.ServerInfo?.Name} {client.ServerInfo?.Version}, protocol {client.NegotiatedProtocolVersion}");

var json = new JsonSerializerOptions { WriteIndented = true };
switch (args[0])
{
    case "list":
        foreach (var tool in await client.ListToolsAsync())
        {
            var a = tool.ProtocolTool.Annotations;
            Console.WriteLine($"- {tool.Name}: \"{a?.Title}\" readOnly={a?.ReadOnlyHint} idempotent={a?.IdempotentHint} " +
                $"openWorld={a?.OpenWorldHint} outputSchema={(tool.ProtocolTool.OutputSchema is null ? "none" : "yes")}");
            Console.WriteLine($"  {tool.Description}");
            Console.WriteLine($"  input: {tool.JsonSchema.GetRawText()}");
        }

        return 0;
    case "search":
        return Print(await client.CallToolAsync("search_regulations", new Dictionary<string, object?>
        {
            ["query"] = args[1],
            ["topK"] = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 5,
        }));
    case "passage":
        return Print(await client.CallToolAsync("get_passage", new Dictionary<string, object?>
        {
            ["chunkId"] = long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture),
        }));
    case "ask":
        return Print(await client.CallToolAsync("ask_regulations", new Dictionary<string, object?> { ["question"] = args[1] }));
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 2;
}

int Print(CallToolResult result)
{
    if (result.IsError == true)
    {
        Console.WriteLine("Tool error: " + string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text)));
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(result.StructuredContent, json));
    return 0;
}
