using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using OllamaSharp;
using RegLens.Application.Answers;
using RegLens.Application.Ingestion;
using RegLens.Application.Search;
using RegLens.Infrastructure.Answers;
using RegLens.Infrastructure.Corpus;
using RegLens.Infrastructure.Embeddings;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ConnectionStringName = "RegLens";
    public const string RedisConnectionStringName = "Redis";

    private const string OllamaHttpClientName = "ollama";

    /// <summary>Registers the adapters behind the Application ports and binds their options (spec 0001, R1.6).</summary>
    public static IServiceCollection AddRegLensInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CorpusOptions>().BindConfiguration(CorpusOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OllamaOptions>().BindConfiguration(OllamaOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmbeddingOptions>().BindConfiguration(EmbeddingOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SearchOptions>().BindConfiguration(SearchOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(_ => BuildDataSource(configuration));
        services.AddSingleton<SqlMigrationRunner>();
        services.AddSingleton<IChunkStore, PostgresChunkStore>();
        services.AddSingleton<IChunkSearch, PostgresChunkSearch>();
        services.AddSingleton<ICorpusCatalog, ManifestCatalog>();
        services.AddSingleton<IDocumentTextSource, PdfPigTextSource>();

        // Retry with backoff, timeouts and a circuit breaker around every Ollama call (R1.20).
        var ollama = configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions();
        services.AddHttpClient(OllamaHttpClientName, (provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<IOptions<OllamaOptions>>().Value.Endpoint;
            })
            // Retrying POST /api/embed is safe: embedding the same text is a pure function.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(ollama.AttemptTimeoutSeconds);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(ollama.TotalTimeoutSeconds);
                // The circuit breaker must sample at least twice the attempt timeout.
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(ollama.AttemptTimeoutSeconds * 2);
            });
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(provider => new OllamaApiClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaHttpClientName),
            provider.GetRequiredService<IOptions<OllamaOptions>>().Value.EmbeddingModel));

        return services;
    }

    /// <summary>
    /// The answer path, for the API only (spec 0003, R3.11–R3.13): Claude behind <see cref="IChatClient"/>,
    /// HybridCache with Redis as its second level, full passages and the index version.
    /// </summary>
    public static IServiceCollection AddRegLensAnswers(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AnswerOptions>().BindConfiguration(AnswerOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IPassageReader, PostgresPassageReader>();
        services.AddSingleton<IIndexVersion, PostgresIndexVersion>();

        // The key comes from ANTHROPIC_API_KEY (P13). One retry layer: the SDK's own (P10).
        services.AddSingleton(provider =>
        {
            var answers = provider.GetRequiredService<IOptions<AnswerOptions>>().Value;
            return new AnthropicClient
            {
                Timeout = TimeSpan.FromSeconds(answers.RequestTimeoutSeconds),
                MaxRetries = answers.MaxRetries,
            };
        });
        services.AddSingleton<IChatClient>(provider =>
        {
            var answers = provider.GetRequiredService<IOptions<AnswerOptions>>().Value;
            return new AnthropicFailureTranslation(
                provider.GetRequiredService<AnthropicClient>().AsIChatClient(answers.Model, answers.MaxOutputTokens));
        });

        // HybridCache uses a registered IDistributedCache as its second level (HybridCache docs).
        var redis = configuration.GetConnectionString(RedisConnectionStringName);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redis);
        }

        services.AddHybridCache();
        return services;
    }

    private static NpgsqlDataSource BuildDataSource(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not set. Use user-secrets or the env var ConnectionStrings__{ConnectionStringName}.");
        }

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    }
}
