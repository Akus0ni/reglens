# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

RegLens is a .NET 10 API that answers questions about Indian NBFC regulation with verified citations and exposes
itself as an MCP server. **Built so far: specs 0001 ingestion, 0002 hybrid retrieval** (`GET /search`), **0003
answers** (`POST /ask`), **0004 evals** (golden set, BM25) **and 0005 contextual retrieval** (Haiku 5.5 chunk context,
heading fixes). Next: spec 0006, MCP and production basics. Plan, decisions
(D*n*) and open questions (Q*n*): `docs/plan.md`. Specs: `docs/specs/`. History: `docs/project-log.md`.

## Workflow — read first

- **Load the `reglens-spec` skill** (`.claude/skills/reglens-spec/SKILL.md`) before designing, coding, reviewing or
  touching git. It holds the binding rules and the feature workflow.
- Every feature: **spec PR** (`spec/NNNN-name`, `docs/specs/NNNN-name.md` from the skill's `spec-template.md`) →
  owner approves and merges → **code PR** (`feat/NNNN-name`). Stop after opening each PR; only the owner merges.
- `main` never takes direct commits. A PreToolUse hook (`.claude/settings.json` → `.claude/hooks/guard-main.mjs`,
  needs Node) blocks `git commit`/`git push` on `main`, pushes to `main`, and `gh pr merge`. GitHub enforces the same
  with the "Protect main" ruleset (`scripts/protect-main.ps1`).
- The owner's standing rules: **ask when in doubt, don't assume**, and **verify against primary sources and cite
  them**. APIs of the `Anthropic`, OllamaSharp and MCP C# SDK packages changed in 2025–2026; check current docs.

## Commands

```powershell
docker compose up -d                              # Postgres+pgvector (127.0.0.1:5432), Redis (127.0.0.1:6379), Aspire dashboard (http://localhost:18888)
ollama pull nomic-embed-text                      # Ollama runs natively on http://localhost:11434
dotnet user-secrets set "ConnectionStrings:RegLens" "Host=localhost;Port=5432;Database=reglens;Username=reglens;Password=reglens-dev" --project src/RegLens.Ingest

dotnet build RegLens.slnx                         # must be 0 warnings (warnings are errors)
dotnet test                                       # all tests; Docker must be running (Testcontainers)
dotnet test --project tests/RegLens.Domain.Tests                                   # one project
dotnet test --project tests/RegLens.Domain.Tests -- --filter-class "*PassageSplitterTests"   # one class
dotnet test --project tests/RegLens.Domain.Tests -- --filter-method "*KeepsACorrigendumWhole" # one test

dotnet run --project src/RegLens.Ingest           # migrate + ingest; run from the repo root (corpus/ is relative)
                                                  # the API never migrates: run this after pulling new migrations
dotnet run --project src/RegLens.Ingest -- --force --document dpdp-rules-2025
                                                  # Context:Enabled (default true) calls claude-haiku-5-5 per chunk: needs
                                                  # ANTHROPIC_API_KEY, ~$0.25 for the corpus; contexts are cached in Postgres
$env:Context__Enabled='false'; dotnet run --project src/RegLens.Ingest   # rebuild without context: free, no API call
dotnet run scripts/chunk-diff.cs -- diff before.json   # re-chunk the PDFs and diff against an export of stored chunks

dotnet user-secrets set "ConnectionStrings:RegLens" "<same as above>" --project src/RegLens.Api
dotnet run --project src/RegLens.Api              # http://127.0.0.1:5180 (5080 is the owner's AgencyOS)
curl "http://127.0.0.1:5180/search?q=Rule%207&topK=5"
curl -X POST http://127.0.0.1:5180/ask -H "Content-Type: application/json" -d '{"question":"When must a breach be reported to the Board?"}'

dotnet run --project src/RegLens.Evals -- retrieval --config hybrid-bm25     # free: recall@5/@8, MRR@10 (n = 20)
dotnet run --project src/RegLens.Evals -- floor --config hybrid-bm25         # D48 floor from the latest retrieval run
dotnet run --project src/RegLens.Evals -- answers --config hybrid-bm25 --confirm-spend  # ~$0.5, writes judge-check.md
                                                  # (0004's filled sheet is judge-check-0004.md; hand checks: hand-checks.json)
dotnet run --project src/RegLens.Evals -- probe --confirm-spend               # prompt-injection probe, ~$0.01
                                                  # configs: dense, keyword-tsrank, keyword-bm25, hybrid-tsrank, hybrid-bm25

./scripts/protect-main.ps1                        # (re)apply the main ruleset; needs gh logged in as the owner
scripts/delete-merged-branch.sh <branch> <pr>     # after the owner merges a PR
```

The dev password `reglens-dev` is the compose default for a localhost-only container; override with
`REGLENS_PG_PASSWORD`. The first embedding after Ollama unloads the model takes ~30 s (model load), which is why
`Ollama:AttemptTimeoutSeconds` defaults to 60.

## Environment

- Windows 11, PowerShell 5.1 and Git Bash. .NET SDKs 8, 9 and 10 installed; target .NET 10.
- Ollama is installed on D:, with models in `D:\OllamaModels` (`OLLAMA_MODELS` user variable).
- `ANTHROPIC_API_KEY` is a Windows user environment variable, read by the Anthropic SDK; never put it in
  `appsettings*.json` or user-secrets. A terminal opened before the variable was set does not see it; start the API
  from a new terminal, or load it with `[Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','User')`.

## Architecture

Clean architecture (D22), dependencies pointing inward and enforced by `tests/RegLens.Architecture.Tests`:
`RegLens.Domain` (pure: corpus types, cleaning, chunking) ← `RegLens.Application` (use cases + the ports they own,
`Microsoft.Extensions.AI` and HybridCache abstractions) ← `RegLens.Infrastructure` (Npgsql/pgvector, SQL migrations,
PdfPig, OllamaSharp, Anthropic SDK, HybridCache + Redis, manifest) ← hosts `RegLens.Ingest` (console) and `RegLens.Api`
(web; `AddRegLensAnswers` wires the answer path for the API only). Hosts are composition roots only.

- **Ingest** (built, spec 0001): `ManifestCatalog` verifies every file's size and SHA-256 before any write →
  PdfPig `ContentOrderTextExtractor` (only `ingest_pages`) → `TextCleaner` drops TOC lines, page numbers, non-Latin
  lines and repeated page headers → a structure strategy per document (`manifest.json` `structure`) cuts one passage
  per numbered paragraph/rule with its heading trail; numbers and chapters must arrive in order, which keeps
  table-of-contents leftovers and stray numbered lines out → passages over 500 estimated tokens split at
  sub-clauses (`Para 5, part 3 of 14`) → `ChunkEmbedder` embeds through a bounded `Channel<T>` →
  `PostgresChunkStore` replaces a changed document in one transaction. A document is skipped only when both its file
  hash and its pipeline (`documents.pipeline`, e.g. `chunker 2; context claude-haiku-5-5 v1`) match (spec 0005, P6).
  `DocumentChunker.Version` and `ContextPrompt.Version` must be bumped whenever chunks or contexts would change.
- **Contextual retrieval** (built, spec 0005): `ChunkContextualizer` gives each chunk a short context (Anthropic: "usually 50-100 tokens") from
  `claude-haiku-5-5` (effort low) with Anthropic's cookbook prompt. The whole document is the first user block with a
  5-minute cache breakpoint (`WithCacheControl`, only honoured by the `AsIChatClient` client); a document's first chunk
  goes alone so the rest read the cache. Contexts are saved to `chunk_contexts` by input hash as they arrive (a re-run
  pays only for missing ones); any failure leaves the stored document untouched. The context is embedded
  (`search_document: ` + context + blank line + content) and keyword-indexed (`tsv`), but **never sent to the answer
  model** (D52). `Context:Enabled=false` rebuilds without it.
- **Search** (built, spec 0002, `GET /search`): `SearchRegulations` embeds `search_query: ` + question (falls back to
  keyword-only with `degraded` if Ollama fails) → `PostgresChunkSearch` runs one SQL statement: pgvector top-40 and
  full-text top-40 (OR of the question's lexemes; `tsv` covers context, heading, para_ref and content) fused with
  RRF k = 60 → citations in the question ("Rule 7", "para 6") are looked up exactly and pinned first. Every hit
  carries dense rank, keyword rank, fused score and similarity. The keyword side ranks by BM25 computed in SQL from
  materialized views (`lexeme_stats`, `chunk_lengths`, `corpus_stats`) that ingestion refreshes (spec 0004, D49);
  `Search:Mode` and `Search:KeywordScoring` switch configurations for evals.
- **Ask** (built, spec 0003, `POST /ask`): `AnswerQuestion` looks up HybridCache (L1 memory, L2 Redis; key =
  prompt version + model + effort + index version + topK + sha256 of the normalised question) → `SearchRegulations`
  → abstain without a Claude call if the best `similarity` is below the floor and nothing is pinned → full passages
  with their heading paths to `claude-sonnet-5-5` as escaped `<passage>` data with a JSON schema (`AnswerPrompt` v3) → `CitationVerifier` (Domain)
  drops citations whose chunk was not sent or whose quote is not in it (normalised substring, ≥ 20 chars) → answer,
  or abstention (`low-similarity`, `model-abstained`, `unverified`, `model-refused`). Refusals and degraded answers
  are not cached. `AnthropicFailureTranslation` maps SDK outages to 503; truncated or unparsable output is 502.
  `GET /search` and the MCP `search_regulations` tool stop after retrieval and never call Anthropic.
- **MCP** (planned, `/mcp`): read-only tools over the same services, consumed by the Day 2 Python agent (separate repo).
- **Evals** (built, spec 0004): `evals/golden-set.json` (owner-reviewed) → `src/RegLens.Evals` (third composition
  root; never in `dotnet test`; paid commands need `--confirm-spend`; no Redis, so every answer is really asked) →
  raw runs in `evals/runs/`, generated `evals/results.md`, `evals/judge-check.md` for the owner. The Haiku judge
  goes through `DropTopP` because Claude 4.x rejects temperature plus top_p.
- Model-call boundary to keep: Ollama only makes vectors; Anthropic only reads and writes text; Postgres never calls
  a model.
