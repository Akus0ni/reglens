using System.ComponentModel.DataAnnotations;

namespace RegLens.Application.Search;

/// <summary>Search settings (spec 0002, P4, P7). Section "Search".</summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>The nomic-embed-text task prefix for questions (model card).</summary>
    public string QueryPrefix { get; set; } = "search_query: ";

    /// <summary>Candidates fetched by each side before fusion; matches pgvector's default hnsw.ef_search of 40.</summary>
    [Range(1, 1000)]
    public int CandidatesPerSide { get; set; } = 40;

    [Range(1, 100)]
    public int DefaultTopK { get; set; } = 8;

    [Range(1, 100)]
    public int MaxTopK { get; set; } = 20;

    [Range(1, 2000)]
    public int MaxQueryLength { get; set; } = 500;
}
