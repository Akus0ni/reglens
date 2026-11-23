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
| D3 | `claude-sonnet-5-5` for answers and agent steps; `claude-haiku-4-5` for bulk work (chunk context) and as the eval judge | Owner | 2026-11-23 |
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
| D27 | `RegLens.Api` is created in spec 0002, bound to 127.0.0.1, without an API key; the key and rate limits come with spec 0005 | Owner | 2026-11-23 |
| D28 | Citations in a query ("Rule 7", "para 6") are looked up exactly and shown first; `para_ref` and `heading` join the keyword index (spec 0002) | Owner | 2026-11-23 |
| D29 | Every search hit carries its dense rank, keyword rank and fused score (spec 0002) | Owner | 2026-11-23 |
| D30 | No document filter in spec 0002; a filter gets its own spec using pgvector iterative scans | Owner | 2026-11-23 |

## 3. Open questions

| # | Question | Blocks |
|---|----------|--------|
| Q1 | Add the DPDP Act 2023 to the corpus? (plan says "if time allows") | Golden-set questions on consent |
| Q3 | How is the API key provisioned and checked (user-secrets, header name, rotation)? | Spec 0005 |
| Q4 | Name and visibility of the Day 2 agent repo | Day 2 |
| Q5 | Langfuse Cloud (Hobby) or self-hosted for agent traces | Day 2 tracing |
| Q6 | Set `OLLAMA_KEEP_ALIVE` (e.g. `24h`) on the owner's machine so the embedding model stays loaded between searches? | Nothing (cold searches wait ~30 s) |

## 4. Specs (proposed order)

| Spec | Feature | Sprint phase |
|------|---------|--------------|
| 0001 | Ingest the corpus: extract, chunk by paragraph, embed, upsert | D1·2 |
| 0002 | Hybrid retrieval with RRF and `GET /search` | D1·3 |
| 0003 | Answers with verified citations, abstention, answer cache | D1·4 |
| 0004 | Golden set and evals (three configs, contextual retrieval) | D1·5 |
| 0005 | MCP server, tracing and production basics | D1·6 |

Numbers are proposals; a spec gets its number when its spec PR is opened.
