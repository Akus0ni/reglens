-- 002_search_text: headings and paragraph references join the keyword index (spec 0002, R2.1, P2).
-- Postgres 17 recomputes the generated column for every row; no chunk is re-embedded.
ALTER TABLE chunks ALTER COLUMN tsv SET EXPRESSION AS (
    to_tsvector('english', coalesce(context, '') || ' ' || heading || ' ' || para_ref || ' ' || content)
);
