# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

RegLens is a .NET 10 API that answers questions about Indian NBFC regulation with verified citations and exposes
itself as an MCP server. **Built so far: spec 0001, ingestion** (corpus → chunks with embeddings in Postgres). Next:
spec 0002, hybrid retrieval. Plan, decisions (D*n*) and open questions (Q*n*): `docs/plan.md`. Specs: `docs/specs/`.
History: `docs/project-log.md`.

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
docker compose up -d                              # Postgres+pgvector (127.0.0.1:5432) and Aspire dashboard (http://localhost:18888)
ollama pull nomic-embed-text                      # Ollama runs natively on http://localhost:11434
dotnet user-secrets set "ConnectionStrings:RegLens" "Host=localhost;Port=5432;Database=reglens;Username=reglens;Password=reglens-dev" --project src/RegLens.Ingest

dotnet build RegLens.slnx                         # must be 0 warnings (warnings are errors)
dotnet test                                       # all tests; Docker must be running (Testcontainers)
dotnet test --project tests/RegLens.Domain.Tests                                   # one project
dotnet test --project tests/RegLens.Domain.Tests -- --filter-class "*PassageSplitterTests"   # one class
dotnet test --project tests/RegLens.Domain.Tests -- --filter-method "*KeepsACorrigendumWhole" # one test

dotnet run --project src/RegLens.Ingest           # migrate + ingest; run from the repo root (corpus/ is relative)
dotnet run --project src/RegLens.Ingest -- --force --document dpdp-rules-2025

./scripts/protect-main.ps1                        # (re)apply the main ruleset; needs gh logged in as the owner
scripts/delete-merged-branch.sh <branch> <pr>     # after the owner merges a PR
```

The dev password `reglens-dev` is the compose default for a localhost-only container; override with
`REGLENS_PG_PASSWORD`. The first embedding after Ollama unloads the model takes ~30 s (model load), which is why
`Ollama:AttemptTimeoutSeconds` defaults to 60.

## Environment

- Windows 11, PowerShell 5.1 and Git Bash. .NET SDKs 8, 9 and 10 installed; target .NET 10.
- Ollama is installed on D:, with models in `D:\OllamaModels` (`OLLAMA_MODELS` user variable).
- `ANTHROPIC_API_KEY` comes from the environment or user-secrets, never from `appsettings*.json`.

## Architecture

Clean architecture (D22), dependencies pointing inward and enforced by `tests/RegLens.Architecture.Tests`:
`RegLens.Domain` (pure: corpus types, cleaning, chunking) ← `RegLens.Application` (use cases + the ports they own,
`Microsoft.Extensions.AI` abstractions) ← `RegLens.Infrastructure` (Npgsql/pgvector, SQL migrations, PdfPig,
OllamaSharp, manifest) ← hosts `RegLens.Ingest` (console) and, later, `RegLens.Api`. Hosts are composition roots only.

- **Ingest** (built, spec 0001): `ManifestCatalog` verifies every file's size and SHA-256 before any write →
  PdfPig `ContentOrderTextExtractor` (only `ingest_pages`) → `TextCleaner` drops TOC lines, page numbers, non-Latin
  lines and repeated page headers → a structure strategy per document (`manifest.json` `structure`) cuts one passage
  per numbered paragraph/rule with its heading trail; numbers and chapters must arrive in order, which keeps
  table-of-contents leftovers and stray numbered lines out → passages over 500 estimated tokens split at
  sub-clauses (`Para 5, part 3 of 14`) → `ChunkEmbedder` embeds through a bounded `Channel<T>` →
  `PostgresChunkStore` replaces a changed document in one transaction. Unchanged documents are skipped.
  Contextual retrieval (Haiku fills `chunks.context`) is planned for spec 0004.
- **Ask** (planned, `POST /ask`): HybridCache lookup → Ollama embeds the question → one SQL query fuses dense (pgvector HNSW)
  and keyword (`tsvector`) top-40 lists with RRF → abstain early if the best score is low → Sonnet answers from the
  chunks as delimited data with structured citations → a code verifier checks every citation → answer or
  "not found". `GET /search` and the MCP `search_regulations` tool stop after retrieval and never call Anthropic.
- **MCP** (planned, `/mcp`): read-only tools over the same services, consumed by the Day 2 Python agent (separate repo).
- **Evals** (planned): golden set + retrieval metrics (no LLM) + Haiku-judged groundedness/relevance; numbers go to
  `evals/results.md` with n, date and model IDs.
- Model-call boundary to keep: Ollama only makes vectors; Anthropic only reads and writes text; Postgres never calls
  a model.
