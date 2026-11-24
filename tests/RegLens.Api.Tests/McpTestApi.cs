using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using NSubstitute;
using RegLens.Application.Answers;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Api.Tests;

/// <summary>The API with every outside service replaced, and an MCP client connected to it in-process (spec 0006).</summary>
internal sealed class McpTestApi : IDisposable
{
    public const string Quote = "the Data Fiduciary shall intimate the Board without delay";

    public McpTestApi(Action<IWebHostBuilder>? configure = null)
    {
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]));
        var search = Substitute.For<IChunkSearch>();
        search.HybridAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([new SearchHit(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", "", 26, 26, "7. Intimation", 1, 1, 0.0328, 0.77)]);
        search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        var passages = Substitute.For<IPassageReader>();
        passages.GetAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns([]);
        passages.GetAsync(Arg.Is<IReadOnlyList<long>>(ids => ids.Contains(42)), Arg.Any<CancellationToken>())
            .Returns([new StoredPassage(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", "", 26, 26, $"7. Intimation of breach. {Quote}.")]);
        var indexVersion = Substitute.For<IIndexVersion>();
        indexVersion.GetAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid().ToString());
        Chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                $$"""{"answer":"Without delay.","abstained":false,"missingInfo":"","citations":[{"chunkId":42,"quote":"{{Quote}}"}]}"""))
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 300 },
            });

        Factory = TestApi.Create(services =>
        {
            services.RemoveAll<IChunkSearch>().AddSingleton(search);
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>().AddSingleton(generator);
            services.RemoveAll<IPassageReader>().AddSingleton(passages);
            services.RemoveAll<IIndexVersion>().AddSingleton(indexVersion);
            services.RemoveAll<IChatClient>().AddSingleton(Chat);
            services.RemoveAll<IDistributedCache>();
        }, configure);
    }

    public IChatClient Chat { get; } = Substitute.For<IChatClient>();

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>
    /// An MCP client over Streamable HTTP. A null <paramref name="protocolVersion"/> is the SDK default (probe
    /// 2026-07-28, fall back to initialize); "2025-11-25" forces the initialize handshake.
    /// </summary>
    public async Task<McpClient> ConnectAsync(string? protocolVersion = null, bool withKey = true)
    {
        var http = withKey ? TestApi.Client(Factory) : Factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(
            transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: TestContext.Current.CancellationToken);
    }

    public void Dispose() => Factory.Dispose();
}
