using Npgsql;
using Pgvector;
using RegLens.Application.Search;
using RegLens.Domain.Search;

namespace RegLens.Infrastructure.Persistence;

/// <summary>
/// Hybrid search in one SQL statement (spec 0002, R2.4–R2.7): pgvector cosine distance and Postgres full-text,
/// each ranked with row_number(), fused with Reciprocal Rank Fusion (k = 60, Cormack et al. 2009).
/// The SQL fragments are constants; user text only ever travels as parameters.
/// </summary>
public sealed class PostgresChunkSearch(NpgsqlDataSource dataSource) : IChunkSearch
{
    public const int RrfK = 60;
    public const int ExcerptLength = 300;
    private const int MaxCitationMatches = 100;

    // The keyword query is the OR of the question's own lexemes (spec 0002, P1). 'simple' keeps the already
    // stemmed lexemes as they are; a question of only stop words gives NULL and the keyword side is skipped.
    private const string KeywordQuery = """
        q AS (
            SELECT to_tsquery('simple', string_agg(quote_literal(lexeme), ' | ')) AS query
            FROM unnest(to_tsvector('english', @text))
        )
        """;

    private const string DenseCandidates = """
        dense AS (
            SELECT id, row_number() OVER (ORDER BY distance, id) AS r, 1 - distance AS similarity
            FROM (SELECT id, embedding <=> @vector AS distance FROM chunks
                  ORDER BY embedding <=> @vector LIMIT @candidates) d
        )
        """;

    private const string NoDenseCandidates = """
        dense AS (SELECT NULL::bigint AS id, NULL::bigint AS r, NULL::float8 AS similarity WHERE false)
        """;

    private const string KeywordCandidates = """
        lex AS (
            SELECT id, row_number() OVER (ORDER BY score DESC, id) AS r
            FROM (SELECT c.id, ts_rank_cd(c.tsv, q.query) AS score FROM chunks c, q
                  WHERE q.query IS NOT NULL AND c.tsv @@ q.query
                  ORDER BY score DESC, c.id LIMIT @candidates) k
        )
        """;

    private const string Fused = """
        fused AS (
            SELECT id, dense.r AS d_rank, lex.r AS k_rank, dense.similarity,
                   coalesce(1.0 / (@rrf_k + dense.r), 0) + coalesce(1.0 / (@rrf_k + lex.r), 0) AS score
            FROM dense FULL OUTER JOIN lex USING (id)
        )
        """;

    private const string HitColumns = """
        c.id, c.document_id, d.title, c.para_ref, c.heading, c.page_start, c.page_end,
        left(c.content, @excerpt), f.d_rank, f.k_rank, coalesce(f.score, 0)::float8, f.similarity
        """;

    public async Task<IReadOnlyList<SearchHit>> HybridAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sql = $"""
            WITH {KeywordQuery}, {Dense(query)}, {KeywordCandidates}, {Fused}
            SELECT {HitColumns}
            FROM fused f JOIN chunks c ON c.id = f.id JOIN documents d ON d.id = c.document_id
            ORDER BY f.score DESC, c.id
            LIMIT @limit
            """;
        await using var command = Command(sql, query);
        command.Parameters.AddWithValue("limit", query.Limit);
        return await ReadHitsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchHit>> FindByCitationsAsync(
        IReadOnlyList<Citation> citations, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(query);
        if (citations.Count == 0)
        {
            return [];
        }

        // Matching rules mirror Citation.Matches (spec 0002, R2.3): exact, "<ref>, …", and "…, Item N" for items.
        var sql = $"""
            WITH {KeywordQuery}, {Dense(query)}, {KeywordCandidates}, {Fused}
            SELECT {HitColumns}
            FROM chunks c JOIN documents d ON d.id = c.document_id LEFT JOIN fused f ON f.id = c.id
            WHERE c.para_ref = ANY(@exact) OR c.para_ref LIKE ANY(@prefixes) OR c.para_ref LIKE ANY(@suffixes)
            ORDER BY coalesce(f.score, 0) DESC, c.id
            LIMIT {MaxCitationMatches}
            """;
        await using var command = Command(sql, query);
        command.Parameters.AddWithValue("exact", citations.Select(c => c.Reference).ToArray());
        command.Parameters.AddWithValue("prefixes", citations.Select(c => c.Reference + ", %").ToArray());
        command.Parameters.AddWithValue("suffixes", citations
            .Where(c => c.Match == CitationMatch.PrefixOrSuffix)
            .Select(c => "%, " + c.Reference)
            .ToArray());
        return await ReadHitsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static string Dense(SearchQuery query) => query.HasVector ? DenseCandidates : NoDenseCandidates;

    private NpgsqlCommand Command(string sql, SearchQuery query)
    {
        var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("text", query.Text);
        command.Parameters.AddWithValue("candidates", query.CandidatesPerSide);
        command.Parameters.AddWithValue("rrf_k", RrfK);
        command.Parameters.AddWithValue("excerpt", ExcerptLength);
        if (query.HasVector)
        {
            command.Parameters.AddWithValue("vector", new Vector(query.Vector));
        }

        return command;
    }

    private static async Task<IReadOnlyList<SearchHit>> ReadHitsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            hits.Add(new SearchHit(
                ChunkId: reader.GetInt64(0),
                DocumentId: reader.GetString(1),
                DocumentTitle: reader.GetString(2),
                ParaRef: reader.GetString(3),
                Heading: reader.GetString(4),
                PageStart: reader.GetInt32(5),
                PageEnd: reader.GetInt32(6),
                Excerpt: reader.GetString(7),
                DenseRank: reader.IsDBNull(8) ? null : (int)reader.GetInt64(8),
                KeywordRank: reader.IsDBNull(9) ? null : (int)reader.GetInt64(9),
                FusedScore: reader.GetDouble(10),
                Similarity: reader.IsDBNull(11) ? null : reader.GetDouble(11)));
        }

        return hits;
    }
}
