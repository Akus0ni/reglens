# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

RegLens is a .NET 10 API that answers questions about Indian NBFC regulation with verified citations and exposes
itself as an MCP server. **No code yet**: the repo holds an empty `RegLens.slnx`, the corpus in `corpus/`, and the
workflow files. The first feature is spec 0001 (ingestion). Plan, decisions (D*n*) and open questions (Q*n*):
`docs/plan.md`. History: `docs/project-log.md`.

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
dotnet build RegLens.slnx                         # only the empty solution exists so far
./scripts/protect-main.ps1                        # (re)apply the main ruleset; needs gh logged in as the owner

# Local infrastructure (from the sprint plan)
docker run -d --name reglens-pg -e POSTGRES_PASSWORD=devpass -p 5432:5432 pgvector/pgvector:pg17
docker run -d --name aspire-dashboard -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest
ollama pull nomic-embed-text                      # Ollama serves on http://localhost:11434
```

Add the test commands (including how to run a single test) here when the first test project lands.

## Environment

- Windows 11, PowerShell 5.1 and Git Bash. .NET SDKs 8, 9 and 10 installed; target .NET 10.
- Ollama is installed on D:, with models in `D:\OllamaModels` (`OLLAMA_MODELS` user variable).
- `ANTHROPIC_API_KEY` comes from the environment or user-secrets, never from `appsettings*.json`.

## Architecture (planned, see `docs/plan.md` D9–D16)

- **Ingest** (offline): PdfPig extracts each `corpus/manifest.json` document (only `ingest_pages` where set) → chunks
  along paragraph/clause numbers with doc id, paragraph ref and page → Haiku adds 50–100 tokens of context per chunk
  (document prompt-cached) → Ollama embeds through a bounded `Channel<T>` → idempotent upsert into Postgres `chunks`
  keyed on content hash.
- **Ask** (`POST /ask`): HybridCache lookup → Ollama embeds the question → one SQL query fuses dense (pgvector HNSW)
  and keyword (`tsvector`) top-40 lists with RRF → abstain early if the best score is low → Sonnet answers from the
  chunks as delimited data with structured citations → a code verifier checks every citation → answer or
  "not found". `GET /search` and the MCP `search_regulations` tool stop after retrieval and never call Anthropic.
- **MCP** (`/mcp`): read-only tools over the same services, consumed by the Day 2 Python agent (separate repo).
- **Evals**: golden set + retrieval metrics (no LLM) + Haiku-judged groundedness/relevance; numbers go to
  `evals/results.md` with n, date and model IDs.
- Model-call boundary to keep: Ollama only makes vectors; Anthropic only reads and writes text; Postgres never calls
  a model.
