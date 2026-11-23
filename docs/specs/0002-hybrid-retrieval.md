# Spec 0002 — Hybrid retrieval and `GET /search`

> Status: **Approved (PR #7, 2026-11-23) · Implemented in `feat/0002-hybrid-retrieval`** · Created: 2026-11-23 · Owner: @Akus0ni
> Depends on: spec 0001 (chunks, embeddings, `tsv`) · Blocked by open questions: none (Q3 stays open for spec 0005)
> Sprint phase: D1·3

## 1. Context

Spec 0001 stored 389 chunks, each with a 768-dimension vector and a `tsvector`. This spec turns them into a
search: **vector search** finds passages by meaning, **keyword search** finds exact terms, and
**Reciprocal Rank Fusion (RRF)** merges the two rankings (D11). It also creates the web host, `RegLens.Api`, with
one endpoint, `GET /search`, which later specs (answers, MCP) build on.

Three things were measured on the real data on 2026-11-23 before writing this spec:

**1. Postgres keyword search ANDs every word by default**, so natural-language questions mostly match nothing:

| Query | All words (`websearch_to_tsquery`) | Any word (OR of lexemes) | Top 3 with OR, by `ts_rank_cd` |
|-------|-----------------------------------|---------------------------|--------------------------------|
| Can a digital lending app access the borrower's phone contacts? | 0 | 103 | Para 12; Para 7; Para 4, part 2 of 7 |
| What is a Key Fact Statement? | 2 | 18 | First Schedule, Part B, Item 1; Para 9, part 1 of 2; Para 59 |
| para 6 digital lending | 0 | 95 | Para 4, part 2 of 7; Para 6; Para 5, part 4 of 14 |
| When must a personal data breach be reported to the Board? | 0 | 183 | **Rule 7**; Rule 6; Second Schedule, part 1 of 2 |
| video based customer identification process | 2 | 140 | Para 27, part 3 of 5; Para 5, part 9 of 14; Para 5, part 11 of 14 |
| Rule 7 | 10 | 98 | Fifth Schedule, Item 6; Para 49; Para 64 |

**2. Citations are not in the index.** "Rule 7" does not rank Rule 7 first, because `para_ref` is not part of `tsv`.

**3. The passage on apps reading phone data** is Credit Facilities **Para 13** ("Collection, usage and sharing of
data with third parties", page 19). It is the target for the paraphrase acceptance check (A5).

## 2. Decisions

Builds on: D4 (nomic-embed-text), D10 (schema), D11 (dense top-40 + keyword top-40, RRF k = 60, top-8),
D15 (SELECT-only API role, built in 0005), D22 (clean architecture), D24 (SQL migrations), D25 (conventions).

New, decided by the owner on 2026-11-23:

| # | Decision |
|---|----------|
| D27 | **`RegLens.Api` is created in this spec**, bound to `127.0.0.1` only, **without an API key**. The key and rate limits come with spec 0005 (Q3 stays open until then) |
| D28 | **Citations in a query are looked up exactly** ("Rule 7", "para 6", "Annex I") and shown first, **and** `para_ref` and `heading` join the keyword index |
| D29 | **Every hit carries its dense rank, keyword rank and fused score**, so it is visible why it ranked |
| D30 | **No document filter in 0002.** With HNSW, filters apply after the index scan (pgvector README); a filter will get its own spec using iterative scans |

Proposed by this spec — **confirm by approving the spec PR**, or say which to change:

| # | Proposal | Why |
|---|----------|-----|
| P1 | The keyword side matches **any** query word (OR of the query's lexemes), ranked by `ts_rank_cd` | Measured above: requiring all words gave 0 hits for 3 of 4 natural questions, which would leave hybrid search with only one side |
| P2 | Migration `002` changes `tsv` to `context + heading + para_ref + content` with Postgres 17 `ALTER COLUMN … SET EXPRESSION`; no re-ingest or re-embedding | Puts chapter words ("Digital Lending") and citations into keyword search |
| P3 | Citation patterns: `Para N` / `Paragraph N`, `Rule N`, `Item N`, `Annex <I…>`, `<First…> Schedule`. A citation matches `para_ref` exactly or as the start of a split part (`Para 6` matches `Para 6, part 2 of 3`, never `Para 60`). Matches come first, ordered by fused score; the rest follow in RRF order; no duplicates; total = `topK` | Precise answers for "Rule 7" while keeping ranking for the rest |
| P4 | The question is embedded as `"search_query: " + question` | The Nomic model card's query prefix, the counterpart of 0001's `search_document: ` |
| P5 | **Graceful degradation:** if the query embedding fails or times out, `/search` returns keyword-only results with `"degraded": "vector search unavailable"` and HTTP 200, not a 500 | Ollama being down should not take search down; the response says what is missing |
| P6 | The API warms the model at startup (one background embedding), and its Ollama attempt timeout is 45 s | A cold model load measured 32 s in spec 0001; the warm-up makes the first real search fast |
| P7 | `topK` default 8, allowed 1–20; each side fetches 40 candidates (matches `hnsw.ef_search`'s default of 40) | Enough candidates for RRF without tuning the index yet |
| P8 | A hit's `excerpt` is the first 300 characters of the chunk; the full text comes later through `get_passage` (spec 0005) | Keeps responses small for MCP clients |
| P9 | `q` must be 1–500 characters after trimming; otherwise HTTP 400 with Problem Details | Validation at the edge |
| P10 | Until spec 0005 creates the SELECT-only role, the API uses the same connection string as ingestion, read from its own user-secrets | One step at a time; D15 still applies |

## 3. Scope

**In scope:** migration 002; citation parsing; hybrid SQL with RRF; reference lookup; the search use case;
`RegLens.Api` with `GET /search` on `127.0.0.1:5180`; model warm-up; keyword-only fallback; tests; latency
measurement.

**Out of scope:** API key, rate limiting, health checks, DB roles, OpenTelemetry export (spec 0005); answers with
citations and caching (spec 0003); document filters (D30); reranking; tuning `ef_search` or the RRF constant.

## 4. Requirements

### 4.1 Index

| ID | Requirement |
|----|-------------|
| R2.1 | Migration `002_search_text.sql` sets `tsv` to `to_tsvector('english', coalesce(context,'') \|\| ' ' \|\| heading \|\| ' ' \|\| para_ref \|\| ' ' \|\| content)` with `ALTER TABLE chunks ALTER COLUMN tsv SET EXPRESSION AS (…)`. The GIN index keeps working; no chunk is re-embedded |

### 4.2 Citations (Domain, pure)

| ID | Requirement |
|----|-------------|
| R2.2 | `CitationQuery.Parse(text)` returns the citations in a query, case-insensitive: `para 6`, `paragraph 6`, `Para 6(2)` → `Para 6`; `rule 7` → `Rule 7`; `item 4` → `Item 4`; `annex I` / `annex 1` → `Annex I`; `first schedule` → `First Schedule`. No citation → empty list |
| R2.3 | A citation matches a chunk when `para_ref` equals it, or starts with it followed by `,` (`Para 6, part 1 of 2`; `First Schedule, Part B, Item 1`), or ends with `, <citation>` for items (`… Part A, Item 4`). `Para 6` never matches `Para 60` |

### 4.3 Hybrid search (Infrastructure SQL)

| ID | Requirement |
|----|-------------|
| R2.4 | One SQL statement computes the dense list (`embedding <=> $vector`, top 40) and the keyword list (`tsv @@ q` ranked by `ts_rank_cd`, top 40, OR of the question's lexemes per P1), ranks each with `row_number()`, joins them with `FULL OUTER JOIN`, and scores `1/(60 + dense_rank) + 1/(60 + keyword_rank)` (missing side = 0) |
| R2.5 | Each result carries chunk id, document id, document title, para_ref, heading, page_start, page_end, excerpt (P8), dense rank (or null), keyword rank (or null) and fused score |
| R2.6 | A question with no indexable words (only stop words) skips the keyword side rather than erroring |
| R2.7 | The reference lookup returns all chunks matching any citation (R2.3), with their dense rank, keyword rank and fused score when they also appear in the hybrid lists (otherwise null / 0) |

### 4.4 Search use case (Application)

| ID | Requirement |
|----|-------------|
| R2.8 | `SearchRegulations.RunAsync(question, topK)` embeds the question with the `search_query: ` prefix (P4), runs the hybrid search and, if the question cites something, the reference lookup, then merges per P3 |
| R2.9 | If embedding throws or times out, it runs keyword-only search and the result says `degraded: "vector search unavailable"` (P5). Cancellation by the caller is not degradation and propagates |
| R2.10 | Hits that matched a citation are marked `pinned: true` |

### 4.5 API host

| ID | Requirement |
|----|-------------|
| R2.11 | `RegLens.Api` (ASP.NET Core minimal API, composition root only) listens on `http://127.0.0.1:5180` by default |
| R2.12 | `GET /search?q=<text>&topK=<n>` returns `{ query, degraded, hits: [ { chunkId, documentId, documentTitle, paraRef, heading, pageStart, pageEnd, excerpt, denseRank, keywordRank, fusedScore, pinned } ] }` as JSON (camelCase) |
| R2.13 | Invalid `q` or `topK` (P7, P9) returns HTTP 400 `application/problem+json` naming the parameter |
| R2.14 | At startup the API embeds one fixed warm-up string in the background (P6), logging success or failure; startup never waits for or fails on it |
| R2.15 | The API's Ollama attempt timeout is 45 s (configurable, as in 0001) |

## 5. Design

```text
GET /search ─► SearchRegulations (Application)
                 ├─ CitationQuery.Parse(q)                      (Domain, pure)
                 ├─ IEmbeddingGenerator "search_query: " + q    (Ollama)  ── fails? ─► keyword-only, degraded
                 ├─ IChunkSearch.HybridAsync(q, vector, 40, k)  (Postgres: dense ⨝ keyword, RRF)
                 └─ IChunkSearch.FindByCitationsAsync(...)      (Postgres: para_ref match)
               merge: pinned first (by fused score) → RRF order → dedupe → topK
```

Hybrid SQL shape (P1 builds the OR query from the question's own lexemes; `'simple'` avoids stemming them twice):

```sql
WITH q AS (
  SELECT to_tsquery('simple', string_agg(quote_literal(lexeme), ' | ')) AS query
  FROM unnest(to_tsvector('english', $1))           -- $1 = question text
), dense AS (
  SELECT id, row_number() OVER (ORDER BY embedding <=> $2) AS r   -- $2 = query vector
  FROM (SELECT id, embedding FROM chunks ORDER BY embedding <=> $2 LIMIT 40) d
), lex AS (
  SELECT id, row_number() OVER (ORDER BY ts_rank_cd(tsv, q.query) DESC) AS r
  FROM chunks, q WHERE q.query IS NOT NULL AND tsv @@ q.query
  ORDER BY ts_rank_cd(tsv, q.query) DESC LIMIT 40
)
SELECT id, dense.r, lex.r, coalesce(1.0/(60 + dense.r), 0) + coalesce(1.0/(60 + lex.r), 0) AS fused
FROM dense FULL OUTER JOIN lex USING (id) ORDER BY fused DESC LIMIT $3;
```

The exact SQL is finalised and tested in the code PR; the shape above is what it must do.

Model calls: **Ollama only** (one embedding per search, plus one warm-up at API start). No Anthropic calls.

New packages: none beyond ASP.NET Core (shared framework) and, for tests, `Microsoft.AspNetCore.Mvc.Testing`
(version checked on NuGet in the code PR).

### System design concepts exercised

| Concept | Where | What could go wrong without it |
|---------|-------|--------------------------------|
| Search fusion (RRF) | R2.4 | Comparing cosine distances with `ts_rank_cd` scores directly: the two scales mean nothing to each other |
| Recall vs precision in lexical search | P1 | AND-only keyword search returns nothing for real questions; hybrid quietly becomes vector-only |
| Approximate index trade-offs | P7, D30 | Too few HNSW candidates, or a filter after the scan, silently returns fewer than k results |
| Graceful degradation | P5, R2.9 | One dependency (Ollama) failing turns every search into a 500 |
| Cold start / warm-up | P6, R2.14 | The first user after an idle period waits ~30 s for a model load |
| Validation at the edge | P9, R2.13 | Huge or empty queries reach the database and the embedding model |
| Read/write separation | D27, P10 | The query path holding write access it never needs (closed in 0005) |

## 6. Acceptance criteria

| # | Check | How |
|---|-------|-----|
| A1 | Solution builds with zero warnings | `dotnet build RegLens.slnx` |
| A2 | All tests pass: citation parser, merge/pinning/degradation (substitutes), SQL ranks and fusion on a pgvector container with known vectors, migration 002, `/search` 400s and JSON shape | `dotnet test` |
| A3 | `q=Rule 7` → DPDP `Rule 7` is the first hit, `pinned: true` | `curl "http://127.0.0.1:5180/search?q=Rule%207"` |
| A4 | `q=para 6 digital lending` → a `Para 6` chunk in the top 3 with a keyword rank | curl |
| A5 | `q=Can a loan app go through the people saved in my phone?` → Credit Facilities `Para 13` (any part) in the top 5, with a dense rank | curl |
| A6 | `q=When must a personal data breach be reported to the Board?` → DPDP `Rule 7` in the top 3 | curl |
| A7 | With Ollama stopped, a search returns HTTP 200, `degraded: "vector search unavailable"`, keyword-ranked hits | `ollama stop` / quit Ollama, then curl |
| A8 | `q=` (empty), 501 characters, `topK=0` and `topK=21` each return HTTP 400 Problem Details | curl |

## 7. Measurements

Record in `docs/project-log.md`: p50 and p95 latency over 20 warm `/search` calls (the six questions above plus
14 more), the A3–A6 results with the ranks shown, and the date and model tag.

## 8. Open questions

| # | Question | Blocks |
|---|----------|--------|
| Q6 | Set `OLLAMA_KEEP_ALIVE` on your machine (e.g. `24h`) so the embedding model stays loaded between searches? It is a Windows user setting, like `OLLAMA_MODELS` | Nothing; without it, a search after 5 idle minutes waits for a model load (~30 s) |
| — | Confirm or change P1–P10 by approving or commenting on this PR | Code PR for 0002 |

## 9. Changes made while building

| Where | Change | Why |
|-------|--------|-----|
| R2.11 | Port **5180**, not 5080 (owner's decision, D31) | 5080 is used by the owner's AgencyOS.Server; the tracker's Day 2 MCP URL now says 5180 |
| R2.13 | A `topK` that cannot be parsed (`topK=abc`) also returns 400 Problem Details | In Development, minimal APIs threw on binding failures and the exception handler turned that into a 500; `RouteHandlerOptions.ThrowOnBadRequest = false` |
| A5 | **Fails as written** (see §10) | Para 13 is dense rank 3 but fused rank 21: OR-matched common words ("loan", "app") put other chunks in both lists, and RRF favours chunks in both. `ts_rank_cd` has no IDF. Decision left to the owner |
| A7 | Passes, but a degraded search took 12.3 s | The standard resilience handler retries the unreachable Ollama before giving up; a fail-fast path is a follow-up |

## 10. Results (2026-11-23, live API on the 389-chunk corpus, nomic-embed-text:latest)

| # | Result |
|---|--------|
| A1 | 0 warnings, 0 errors |
| A2 | 108 tests pass (42 new: citation parser, merge/pinning/degradation, SQL ranking on pgvector, `/search` endpoint) |
| A3 | `Rule 7` → DPDP **Rule 7 first, pinned** |
| A4 | `para 6 digital lending` → Credit Facilities **Para 6 first** (dense 1, keyword 2), KYC Para 6 second (both pinned) |
| A5 | ✗ Credit Facilities Para 13 is **dense rank 3** but **fused rank 21**; top 5 are Paras 56, 7, 51, 25, 34 |
| A6 | Breach question → DPDP **Rule 7 first** (dense 1, keyword 1) |
| A7 | Ollama unreachable → HTTP 200, `degraded: "vector search unavailable"`, Rule 7 first by keyword rank; 12.3 s |
| A8 | Empty, 501 chars, `topK=0`, `topK=21` (and `topK=abc`) → HTTP 400 `application/problem+json` |
| Latency | 20 warm searches: **p50 66 ms, p95 78 ms** (min 55, max 82) |

Why A5 fails, measured: "loan" appears in 66 of 389 chunks (17%), "phone" in 2 (0.5%, one of them Para 13). Dropping
words found in more than 10% of chunks did not bring Para 13 into the keyword top 5. A principled fix is IDF weighting
on the keyword side (BM25-style), which belongs with the golden-set evals in spec 0004 rather than tuned on one query.

## 11. Sources (checked 2026-11-23)

- pgvector README: `<=>` is cosine distance; `hnsw.ef_search` default 40; "filtering is applied *after* the index is
  scanned"; iterative index scans — https://github.com/pgvector/pgvector (installed: pgvector 0.8.7)
- PostgreSQL 17, ALTER TABLE: `ALTER [ COLUMN ] column_name SET EXPRESSION AS ( expression )` —
  https://www.postgresql.org/docs/17/sql-altertable.html
- PostgreSQL 17, controlling text search: `websearch_to_tsquery`, `ts_rank_cd`, tsquery operators —
  https://www.postgresql.org/docs/17/textsearch-controls.html
- Cormack, Clarke & Büttcher, Reciprocal Rank Fusion (SIGIR 2009), k = 60 —
  https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf
- Nomic model card, `search_query:` prefix — https://huggingface.co/nomic-ai/nomic-embed-text-v1.5
- Ollama FAQ: models stay loaded 5 minutes by default; `OLLAMA_KEEP_ALIVE` —
  https://github.com/ollama/ollama/blob/main/docs/faq.mdx
- Measurements in §1: run on the local `reglens` database (389 chunks) on 2026-11-23
