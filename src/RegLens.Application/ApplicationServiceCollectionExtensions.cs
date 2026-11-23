using Microsoft.Extensions.DependencyInjection;
using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;

namespace RegLens.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the use cases. Ports (catalog, text source, store, embedding generator) and bound options
    /// (<see cref="EmbeddingOptions"/>) come from Infrastructure.
    /// </summary>
    public static IServiceCollection AddRegLensApplication(this IServiceCollection services)
    {
        services.AddSingleton(_ => DocumentChunker.CreateDefault());
        services.AddSingleton<ChunkEmbedder>();
        services.AddTransient<IngestCorpus>();
        return services;
    }
}
