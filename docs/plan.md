# RegLens plan

Owner: @Akus0ni · Started: 2026-11-23

## 1. Goal

A portfolio project for AI engineering roles: a .NET 10 API that answers questions about Indian financial regulation
with **verified citations**, says "not found" instead of guessing, measures its own retrieval and answer quality, and
exposes itself as an **MCP server**. A separate Python LangGraph agent (Day 2, its own repo) uses it over MCP to review
product features for compliance.

## 2. Decisions

"Owner" = decided by the owner in conversation. "Plan" = from the sprint plan the owner adopted (tracker
`reglens-sprint.html`, kept outside this repo); confirm before changing.

| # | Decision | Source | Date |
|---|----------|--------|------|
| D1 | Two projects: RegLens (.NET, this repo) on Day 1; a Python compliance-review agent on Day 2 that calls RegLens over MCP | Owner | 2026-11-23 |
| D2 | LLMs via the Anthropic Claude API only; everything else free or local | Owner | 2026-11-23 |
| D3 | `claude-sonnet-5-5` for answers and agent steps; `claude-haiku-4-5` for bulk work (chunk context) and as the eval judge (chunk context amended by D50) | Owner | 2026-11-23 |
| D4 | Embeddings run locally in Ollama: `nomic-embed-text` (768 dims), via OllamaSharp's `IEmbeddingGenerator`. Anthropic has no embedding model | Owner | 2026-11-23 |
| D5 | Domain: Indian financial regulation; agent job: compliance review (feature description in, cited findings memo out, human approval before filing) | Owner | 2026-11-23 |
| D6 | Corpus scope is **NBFCs**: RBI (NBFCs – KYC) Directions 2025 and RBI (NBFCs – Credit Facilities) Directions 2025 (Chapter III Digital Lending). RBI repealed the 2016 KYC Master Direction and the standalone Digital Lending Directions on 28 Nov 2025 (DOR.RRC.REC.302/33-01-010/2025-26) | Owner | 2026-11-23 |
| D7 | DPDP Rules 2025 (G.S.R. 846(E)) **and** its corrigendum (G.S.R. 892(E)) from MeitY; not the PIB explainer | Owner | 2026-11-23 |
| D8 | Corpus PDFs are committed in `corpus/` with `corpus/manifest.json` (provenance + SHA-256) | Owner | 2026-11-23 |
| D9 | .NET stack: ASP.NET Core minimal APIs on .NET 10; `Microsoft.Extensions.AI` abstractions; official `Anthropic` NuGet (`AsIChatClient`); OllamaSharp; PdfPig for extraction; Npgsql + `Pgvector`; `HybridCache` for answers | Plan | 2026-11-23 |
| D10 | Postgres 17 + pgvector (`pgvector/pgvector:pg17`). One `chunks` table with `vector(768)` (HNSW, cosine) and a generated `tsvector` (GIN); idempotent upsert keyed on a content hash | Plan | 2026-11-23 |
| D11 | Hybrid retrieval: dense top-40 + keyword top-40 fused with Reciprocal Rank Fusion (k = 60), top-8. Postgres full-text is not BM25 and is never called BM25 | Plan | 2026-11-23 |
| D12 | Contextual retrieval: Haiku writes 50–100 tokens of context per chunk with the whole document prompt-cached; prepended before embedding and indexing | Plan | 2026-11-23 |
| D13 | Answers: structured output with citations; deterministic citation verifier; abstain below a tuned threshold; retrieved text treated as data (OWASP LLM01) | Plan | 2026-11-23 |
| D14 | MCP server via `ModelContextProtocol.AspNetCore` (HTTP, `/mcp`), read-only tools `search_regulations`, `get_passage`, `ask_regulations` | Plan | 2026-11-23 |
| D15 | Ops: OpenTelemetry to the standalone Aspire dashboard; rate limiting; API key; liveness/readiness checks; SELECT-only DB role for the API; standard resilience handler on outbound HTTP | Plan | 2026-11-23 |
| D16 | Evals: 30-question golden set (20 answerable, 10 not); recall@5, MRR, abstention accuracy, groundedness and relevance with a hand-checked Haiku judge; three configs (dense, hybrid, hybrid + contextual); results with n, date, model IDs in `evals/results.md` | Plan | 2026-11-23 |
| D17 | Repo workflow: one spec per feature; **spec PR first, then code PR**; only the owner merges | Owner | 2026-11-23 |
| D18 | `main` protection: ruleset requires a PR approved by a code owner; direct push, force-push and deletion blocked; admin bypass only through a PR (`scripts/protect-main.ps1`) | Owner | 2026-11-23 |
| D19 | Local guard: a Claude Code PreToolUse hook blocks `git commit`/`git push` on `main`, pushes to `main`, and `gh pr merge` | Owner | 2026-11-23 |
| D20 | Docs layout mirrors InvoiceGen: `docs/plan.md`, `docs/specs/NNNN-name.md`, `docs/project-log.md`, `.claude/skills/reglens-spec/` | Owner | 2026-11-23 |
| D21 | Merged branches are deleted: GitHub `delete_branch_on_merge` is on for the remote; locally, Claude deletes the branch after checking its content is on `main` (squash merges need `git branch -D`) | Owner | 2026-11-23 |
| D22 | Clean architecture, 4 layers + 2 hosts: Domain ← Application ← Infrastructure; hosts `RegLens.Ingest` (console) and `RegLens.Api` (web) are composition roots. SOLID; no speculative interfaces (spec 0001) | Owner | 2026-11-23 |
| D23 | Ingestion runs from both the console host and an API trigger, through one use case (resolves Q2; spec 0001) | Owner | 2026-11-23 |
| D24 | Schema via versioned SQL scripts applied in order and recorded in `schema_version` (spec 0001) | Owner | 2026-11-23 |
| D25 | Build and test conventions mirror InvoiceGen: `global.json` + Microsoft.Testing.Platform, Central Package Management, `Directory.Build.props` (warnings as errors), xUnit v3, AwesomeAssertions, NSubstitute, Testcontainers (spec 0001) | Owner | 2026-11-23 |
| D26 | Local infrastructure via `docker-compose.yml` (pgvector Postgres + standalone Aspire dashboard); Ollama runs natively (spec 0001) | Owner | 2026-11-23 |
| D27 | `RegLens.Api` is created in spec 0002, bound to 127.0.0.1, without an API key; the key and rate limits come with spec 0006 (renumbered by D44) | Owner | 2026-11-23 |
| D28 | Citations in a query ("Rule 7", "para 6") are looked up exactly and shown first; `para_ref` and `heading` join the keyword index (spec 0002) | Owner | 2026-11-23 |
| D29 | Every search hit carries its dense rank, keyword rank and fused score (spec 0002) | Owner | 2026-11-23 |
| D30 | No document filter in spec 0002; a filter gets its own spec using pgvector iterative scans | Owner | 2026-11-23 |
| D31 | `RegLens.Api` listens on 127.0.0.1:**5180** (5080 belongs to the owner's AgencyOS.Server) | Owner | 2026-11-23 |
| D32 | Answer citations come from a JSON schema plus our verifier, not Claude's built-in Citations (the API rejects Citations combined with structured outputs) (spec 0003) | Owner | 2026-11-23 |
| D33 | Abstention: Claude decides; the Claude call is skipped when the best similarity is below a floor (0.55 to start, tuned in 0004) (spec 0003) | Owner | 2026-11-23 |
| D34 | A citation that fails verification is dropped and counted; no verified citation left → abstain as "unverified" (spec 0003) | Owner | 2026-11-23 |
| D35 | Answers are served by `POST /ask` with a JSON body `{question, topK?}` (spec 0003) | Owner | 2026-11-23 |
| D36 | Answer model effort `medium`, configurable; 0004 compares low / medium / high (spec 0003) | Owner | 2026-11-23 |
| D37 | `HybridCache` with Redis as the second-level cache; Redis joins `docker-compose.yml` (spec 0003) | Owner | 2026-11-23 |
| D38 | With vector search degraded, `/ask` answers from keyword-only hits, flagged, not cached (spec 0003) | Owner | 2026-11-23 |
| D39 | Quotes are verified as a normalised substring (whitespace, quote and dash variants equal; case and words exact) (spec 0003) | Owner | 2026-11-23 |
| D40 | Spec 0002's A5 gap is accepted; IDF / BM25-style keyword weighting is evaluated in spec 0004 against the golden set | Owner | 2026-11-23 |
| D41 | Add the RBI (NBFCs – Responsible Business Conduct) Directions, 2025 (id 12942, updated as on 1 July 2026), all 36 pages including Annex I (resolves Q7; spec 0004) | Owner | 2026-11-23 |
| D42 | The golden set is drafted by Claude and every row is reviewed by the owner (spec 0004) | Owner | 2026-11-23 |
| D43 | BM25 keyword scoring computed in our SQL (Lucene formula, k1 1.2, b 0.75) from statistics refreshed on ingest; no new extension (spec 0004) | Owner | 2026-11-23 |
| D44 | Contextual retrieval is its own spec (0005); MCP and production basics move to 0006 | Owner | 2026-11-23 |
| D45 | Evals run from a console app `src/RegLens.Evals`, not from `dotnet test` (spec 0004) | Owner | 2026-11-23 |
| D46 | Answer evals at effort medium only (spec 0004) | Owner | 2026-11-23 |
| D47 | Judge: Groundedness + Relevance (Microsoft.Extensions.AI.Evaluation.Quality) with `claude-haiku-4-5`; owner hand-checks 10 judgements (spec 0004) | Owner | 2026-11-23 |
| D48 | Similarity floor = highest with no answerable question refused, minus 0.02 (spec 0004) | Owner | 2026-11-23 |
| D49 | BM25 is the default keyword scoring, chosen from the golden-set results (recall@5 0.85 vs 0.80, MRR@10 0.772 vs 0.637, n = 20); the floor is 0.60 (spec 0004) | Owner | 2026-11-24 |
| D50 | Chunk context is written by `claude-haiku-5-5` (released 2026-10-07; $0.10 / $0.50 per MTok, 512-token cache minimum), not `claude-haiku-4-5`. Amends D3 for chunk context only; the eval judge stays `claude-haiku-4-5` (spec 0005) | Owner | 2026-11-24 |
| D51 | The chunker's heading defects in the RBC Directions are fixed in spec 0005; the context model gets each chunk's heading path, and the answer prompt (v3) gets each passage's heading | Owner | 2026-11-24 |
| D52 | Generated chunk context is used for retrieval only (embedding and BM25), never sent to the answer model (spec 0005) | Owner | 2026-11-24 |
| D53 | Contextual retrieval replaces the index in place and is compared with spec 0004's saved runs; a setting rebuilds without context (spec 0005) | Owner | 2026-11-24 |
| D54 | A context-generation failure fails the document (old chunks stay); generated contexts are cached so a re-run resumes (spec 0005) | Owner | 2026-11-24 |
| D55 | Spend cap for spec 0005's paid runs: $2 | Owner | 2026-11-24 |
| D58 | API key (resolves Q3): one key from `Auth:ApiKey` (env var `Auth__ApiKey` or user-secrets, never appsettings), sent as `X-API-Key`, compared in constant time; health endpoints open; rotate by changing it and restarting (spec 0006) | Owner | 2026-11-24 |
| D59 | Rate limits per caller, fixed one-minute windows: ask bucket 5 (`/ask`, `ask_regulations`), read bucket 30 (`/search`, `/passages`, `search_regulations`, `get_passage`); shared by REST and MCP (spec 0006) | Owner | 2026-11-24 |
| D60 | The API connects as `reglens_api`, a login in the NOLOGIN role `reglens_readonly` (SELECT only) created by migration 005; the login and its password come from `scripts/create-api-db-user.ps1` and an env var (spec 0006) | Owner | 2026-11-24 |
| D61 | Readiness: Postgres or the Ollama embedding model unreachable = Unhealthy; Redis unreachable = Degraded; Anthropic not probed (spec 0006) | Owner | 2026-11-24 |
| D62 | API and Ingest export OpenTelemetry to the Aspire dashboard, with prompt and answer text in spans (`Telemetry:CaptureContent`, default on) (spec 0006) | Owner | 2026-11-24 |
| D63 | `GET /passages/{id}` gives REST the same three operations as MCP (spec 0006) | Owner | 2026-11-24 |
| D64 | Spend cap for spec 0006's paid checks: $0.50 | Owner | 2026-11-24 |
| D56 | hybrid-bm25 stays the default after spec 0005, although dense scored higher (recall@5 0.95 vs 0.90, MRR@10 0.931 vs 0.907, n = 20): the gap is one question, and the keyword side keeps exact-term queries (paragraph numbers, G.S.R. numbers). Revisit with a larger golden set. Confirms D49 | Owner | 2026-11-24 |
| D57 | Generated contexts that invent structure (2 of 512: DPDP Rules 7 and 14 placed in chapters the Rules do not have) are accepted as a known limitation; no prompt change for observed errors (spec 0005 §10, A5) | Owner | 2026-11-24 |

## 3. Open questions

| # | Question | Blocks |
|---|----------|--------|
| Q1 | Add the DPDP Act 2023 to the corpus? (plan says "if time allows") | Golden-set questions on consent |
| Q4 | Name and visibility of the Day 2 agent repo | Day 2 |
| Q5 | Langfuse Cloud (Hobby) or self-hosted for agent traces | Day 2 tracing |
| Q6 | Set `OLLAMA_KEEP_ALIVE` (e.g. `24h`) on the owner's machine so the embedding model stays loaded between searches? | Nothing (cold searches wait ~30 s) |

## 4. Specs (proposed order)

| Spec | Feature | Sprint phase |
|------|---------|--------------|
| 0001 | Ingest the corpus: extract, chunk by paragraph, embed, upsert | D1·2 |
| 0002 | Hybrid retrieval with RRF and `GET /search` | D1·3 |
| 0003 | Answers with verified citations, abstention, answer cache | D1·4 |
| 0004 | Golden set, eval runner, retrieval and answer metrics, BM25 (D40, D43), floor from data (D48), new RBC document (D41) | D1·5 |
| 0005 | Contextual retrieval (Haiku 5.5 context per chunk, re-embed) and heading fixes, measured against 0004's baseline (D44, D50–D55) | D1·5 |
| 0006 | MCP server, tracing and production basics | D1·6 |

Numbers are proposals; a spec gets its number when its spec PR is opened.
