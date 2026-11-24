using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RegLens.Application;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;
using RegLens.Infrastructure.Context;

namespace RegLens.Infrastructure.Tests.Context;

public sealed class AnthropicChunkContextGeneratorTests : IDisposable
{
    // Where AIContentCacheExtensions.WithCacheControl stores the breakpoint (its GetCacheControl is internal in 13.12.0).
    private const string CacheControlKey = "anthropic:cache_control";

    private static readonly Chunk Chunk = new("rbc", "Para 98", "Chapter III › I. Responsibilities", 28, 28, "98. The NBFC shall ensure.");

    private readonly FakeChatClient _chat = new();

    [Fact]
    public async Task PutsTheCacheBreakpointOnTheDocumentBlockOnly() // R5.6, P1
    {
        await Generator().GenerateAsync(new ContextRequest("Whole document.", Chunk), TestContext.Current.CancellationToken);

        var message = _chat.Messages.Should().ContainSingle().Subject;
        message.Role.Should().Be(ChatRole.User);
        message.Contents.Should().HaveCount(2);
        var (document, chunk) = (message.Contents[0].Should().BeOfType<TextContent>().Subject, message.Contents[1].Should().BeOfType<TextContent>().Subject);
        document.Text.Should().Be("<document>\nWhole document.\n</document>");
        document.AdditionalProperties.Should().ContainKey(CacheControlKey)
            .WhoseValue.Should().BeOfType<Anthropic.Models.Messages.CacheControlEphemeral>();
        chunk.Text.Should().Be(ContextPrompt.ChunkBlock(Chunk));
        (chunk.AdditionalProperties?.ContainsKey(CacheControlKey) ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task AsksTheContextModelAtLowEffortWithNoSamplingSettings() // P3
    {
        await Generator().GenerateAsync(new ContextRequest("Doc.", Chunk), TestContext.Current.CancellationToken);

        _chat.Options.Should().Match<ChatOptions>(o => o.ModelId == "claude-haiku-5-5" && o.MaxOutputTokens == 1024
            && o.Reasoning!.Effort == ReasoningEffort.Low
            && o.Temperature == null && o.TopP == null && o.TopK == null);
    }

    [Fact]
    public async Task ReturnsTheTextBlocksAndSplitsTheUsageAsBilled() // P10
    {
        _chat.Response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new TextReasoningContent("thinking…"), new TextContent("Situates Para 98.")]))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = 3839,
                CachedInputTokenCount = 0,
                OutputTokenCount = 60,
                AdditionalCounts = new() { [AnthropicChunkContextGenerator.CacheWriteCountKey] = 3731 },
            },
        };

        var context = await Generator().GenerateAsync(new ContextRequest("Doc.", Chunk), TestContext.Current.CancellationToken);

        context.Text.Should().Be("Situates Para 98.");
        context.Usage.Should().Be(new TokenUsage(108, 3731, 0, 60));
    }

    [Fact]
    public void CountsCacheReadsSeparately()
    {
        AnthropicChunkContextGenerator.Usage(new UsageDetails { InputTokenCount = 3839, CachedInputTokenCount = 3731, OutputTokenCount = 65 })
            .Should().Be(new TokenUsage(108, 0, 3731, 65));
    }

    public void Dispose() => _chat.Dispose();

    private AnthropicChunkContextGenerator Generator() => new(_chat, Options.Create(new ContextOptions()));

    private sealed class FakeChatClient : IChatClient
    {
        public List<ChatMessage> Messages { get; } = [];

        public ChatOptions? Options { get; private set; }

        public ChatResponse Response { get; set; } = new(new ChatMessage(ChatRole.Assistant, "Context."));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            Options = options;
            return Task.FromResult(Response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
