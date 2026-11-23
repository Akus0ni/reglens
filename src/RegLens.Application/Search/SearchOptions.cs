using System.ComponentModel.DataAnnotations;

namespace RegLens.Application.Search;

/// <summary>Which candidate lists feed the fusion (spec 0004, P9). The API uses <see cref="Hybrid"/>.</summary>
public enum SearchMode
{
    Hybrid,
    DenseOnly,
    KeywordOnly,
}

/// <summary>How the keyword side ranks its matches (spec 0004, D43, P9).</summary>
public enum KeywordScoring
{
    /// <summary>Postgres <c>ts_rank_cd</c>: cover density, no inverse document frequency.</summary>
    TsRankCd,

    /// <summary>BM25 with Lucene's idf and defaults (k1 = 1.2, b = 0.75), from statistics refreshed on ingest.</summary>
    Bm25,
}

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

    /// <summary>Evals switch this (P1); the API keeps <see cref="SearchMode.Hybrid"/>.</summary>
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;

    /// <summary>The default stays <c>ts_rank_cd</c> until the owner picks from the eval results (P9).</summary>
    public KeywordScoring KeywordScoring { get; set; } = KeywordScoring.TsRankCd;
}
