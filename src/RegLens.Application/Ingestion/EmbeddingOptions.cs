using System.ComponentModel.DataAnnotations;

namespace RegLens.Application.Ingestion;

/// <summary>Settings for embedding chunks during ingestion (spec 0001, R1.18–R1.19). Section "Embedding".</summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    /// <summary>The nomic-embed-text task prefix for documents (model card).</summary>
    public string DocumentPrefix { get; set; } = "search_document: ";

    [Range(1, 4096)]
    public int Dimensions { get; set; } = 768;

    [Range(1, 1024)]
    public int ChannelCapacity { get; set; } = 32;

    [Range(1, 4)]
    public int Consumers { get; set; } = 2;
}
