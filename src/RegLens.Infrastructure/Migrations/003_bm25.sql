-- 003_bm25: word statistics for BM25 keyword scoring (spec 0004, R4.3, P8).
-- Ingestion refreshes these after it writes a document (R4.4); the API only reads them.

-- Documents (chunks) each lexeme occurs in: BM25's n for the idf term. ts_stat, PostgreSQL 17 docs §12.4.4.
-- The query text names the schema: PostgreSQL 17 runs CREATE/REFRESH MATERIALIZED VIEW with a safe search_path
-- (release notes, "Change functions to use a safe search_path during maintenance operations").
CREATE MATERIALIZED VIEW lexeme_stats AS
    SELECT word AS lexeme, ndoc FROM ts_stat('SELECT tsv FROM public.chunks');
CREATE UNIQUE INDEX lexeme_stats_lexeme ON lexeme_stats (lexeme);

-- Chunk length in tokens: the sum of every lexeme's position count (stop words are not counted).
CREATE MATERIALIZED VIEW chunk_lengths AS
    SELECT c.id,
           coalesce((SELECT sum(coalesce(array_length(u.positions, 1), 1)) FROM unnest(c.tsv) u), 0)::int AS len
    FROM chunks c;
CREATE UNIQUE INDEX chunk_lengths_id ON chunk_lengths (id);

-- N and the average length; one row. Refresh after chunk_lengths.
CREATE MATERIALIZED VIEW corpus_stats AS
    SELECT 1 AS id, count(*)::int AS n, greatest(coalesce(avg(len), 0), 1)::float8 AS avg_len FROM chunk_lengths;
CREATE UNIQUE INDEX corpus_stats_id ON corpus_stats (id);
