using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RegLens.Application.Answers;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Api.Tests;

/// <summary><c>POST /ask</c> with Postgres, Ollama, Claude and Redis replaced (spec 0003, R3.15–R3.16, P11–P12).</summary>
public sealed class AskEndpointTests : IDisposable
{
    private const string Quote = "the Data Fiduciary shall intimate the Board without delay";

    private readonly IChatClient _chat = Substitute.For<IChatClient>();
    private readonly WebApplicationFactory<Program> _factory;

    public AskEndpointTests()
    {
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[768])]));
        var search = Substitute.For<IChunkSearch>();
        search.HybridAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([new SearchHit(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", "", 26, 26, "7. Intimation", 1, 1, 0.0328, 0.77)]);
        search.FindByCitationsAsync(Arg.Any<IReadOnlyList<Citation>>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        var passages = Substitute.For<IPassageReader>();
        passages.GetAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>())
            .Returns([new StoredPassage(42, "dpdp-rules-2025", "DPDP Rules, 2025", "Rule 7", 26, 26, $"7. Intimation of breach. {Quote}.")]);
        var indexVersion = Substitute.For<IIndexVersion>();
        indexVersion.GetAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid().ToString());
        _chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                $$"""{"answer":"Without delay.","abstained":false,"missingInfo":"","citations":[{"chunkId":42,"quote":"{{Quote}}"}]}"""))
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 300 },
            });

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChunkSearch>().AddSingleton(search);
                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>().AddSingleton(generator);
                services.RemoveAll<IPassageReader>().AddSingleton(passages);
                services.RemoveAll<IIndexVersion>().AddSingleton(indexVersion);
                services.RemoveAll<IChatClient>().AddSingleton(_chat);
                services.RemoveAll<IDistributedCache>();
            }));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ReturnsTheAnswerAsCamelCaseJson()
    {
        var response = await Post("""{"question":"When must a breach be reported?","topK":5}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        root.GetProperty("question").GetString().Should().Be("When must a breach be reported?");
        root.GetProperty("answer").GetString().Should().Be("Without delay.");
        root.GetProperty("abstained").GetBoolean().Should().BeFalse();
        root.GetProperty("abstainReason").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("missingInfo").GetString().Should().BeEmpty();
        root.GetProperty("droppedCitations").GetInt32().Should().Be(0);
        root.GetProperty("degraded").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("cached").GetBoolean().Should().BeFalse();
        root.GetProperty("model").GetString().Should().Be("claude-sonnet-5-5");
        root.GetProperty("usage").GetProperty("inputTokens").GetInt64().Should().Be(1200);
        var citation = root.GetProperty("citations")[0];
        citation.GetProperty("chunkId").GetInt64().Should().Be(42);
        citation.GetProperty("documentTitle").GetString().Should().Be("DPDP Rules, 2025");
        citation.GetProperty("paraRef").GetString().Should().Be("Rule 7");
        citation.GetProperty("pageStart").GetInt32().Should().Be(26);
        citation.GetProperty("quote").GetString().Should().Be(Quote);
    }

    [Theory]
    [InlineData("""{"question":""}""", "question")]
    [InlineData("""{"question":"   "}""", "question")]
    [InlineData("""{"topK":5}""", "question")]
    [InlineData("""{"question":"kyc","topK":0}""", "topK")]
    [InlineData("""{"question":"kyc","topK":21}""", "topK")]
    public async Task RejectsInvalidInputWithProblemDetails(string body, string field)
    {
        var response = await Post(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"question":"kyc","topK":"abc"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task RejectsAMissingOrMalformedBody(string body)
    {
        var response = await Post(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectsAQuestionLongerThan500Characters()
    {
        var response = await Post(JsonSerializer.Serialize(new { question = new string('x', 501) }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(AnswerFailure.ModelUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(AnswerFailure.InvalidModelOutput, HttpStatusCode.BadGateway)]
    public async Task MapsModelFailuresToProblemDetails(AnswerFailure failure, HttpStatusCode status)
    {
        _chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AnswerGenerationException(failure, "The answer model is unavailable."));

        var response = await Post("""{"question":"When must a breach be reported?"}""");

        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<HttpResponseMessage> Post(string body)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri("/ask", UriKind.Relative), content, TestContext.Current.CancellationToken);
    }
}
