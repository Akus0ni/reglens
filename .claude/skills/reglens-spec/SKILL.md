---
name: reglens-spec
description: Spec-first workflow and binding rules for RegLens (Akus0ni/reglens), the .NET 10 RAG + MCP API over Indian financial regulations. Use before designing, writing, reviewing or testing any code here, before adding any feature (ingestion, retrieval, answers, evals, MCP tools, tracing), before touching corpus/, and for any branch, commit or PR work. Also use when a requirement is unclear, to check whether it is an open question.
---

# RegLens — spec workflow and rules

Source of truth: `docs/plan.md` (decisions D*n*, open questions Q*n*). One spec per feature in
`docs/specs/NNNN-name.md`. History in `docs/project-log.md`. If this skill and `docs/plan.md` disagree, the plan
wins; fix this skill in the same change.

## Rule 0: ask, don't assume

The owner's standing instruction: **when in doubt or when something needs clarification, ask. Do not assume.**
- Before building anything that depends on an open question (`docs/plan.md` §3), stop and ask with `AskUserQuestion`.
- When you recommend an option, label it "(Recommended)", but let the owner decide.
- Record each answer in `docs/plan.md` (§3 → §2 with the next D number), then update this skill if it is a rule.

## Rule 1: verify, then cite

Library APIs, package names and versions, model IDs, regulatory text and any number put in a spec must come from a
primary source checked in this session (official docs, NuGet, the regulator's site). Put the links in the spec's
**Sources** section. If something could not be verified, say so in the spec instead of guessing. Package APIs here
changed in 2025–2026 (MCP C# SDK 2.x, `Anthropic` NuGet ≥ 10, OllamaSharp); never copy patterns from memory or old
blog posts.

## The feature workflow (spec PR → code PR)

1. **Spec branch.** `git switch -c spec/NNNN-short-name` from an up-to-date `main`. NNNN = next free number in
   `docs/specs/`.
2. **Write the spec** from `spec-template.md` (next to this file). Numbered requirements `R<spec>.<n>` and acceptance
   criteria the owner can check. New owner decisions go in the spec's Decisions table *and* `docs/plan.md`.
3. **Spec PR.** Commit `docs(spec): NNNN short name`, push, `gh pr create` with the PR template. Then **stop** and tell
   the owner the PR link. Do not write feature code until the owner has approved and merged the spec PR.
4. **Code branch.** After the merge, clean up (below), then `git switch -c feat/NNNN-short-name`.
5. **Build to the spec.** Every requirement is implemented or explicitly deferred in the PR body. If the spec turns
   out wrong, change the spec in the same PR and call it out; don't silently diverge.
6. **Code PR.** Conventional Commits (`feat(ingest): …`, `fix(retrieval): …`, `test: …`, `docs: …`, `chore: …`).
   PR title `feat: NNNN short name`; body links `docs/specs/NNNN-…md`, lists each R-number as done/deferred, and shows
   how it was verified (commands run, test output, eval numbers with n). Update `docs/project-log.md` in the same PR.
7. **Never merge.** Only the owner merges. Don't run `gh pr merge`, don't push to `main`, don't use `--admin`.

**After every merge**, delete the branch. GitHub deletes the remote branch automatically (repo setting
`delete_branch_on_merge`). Locally:

```bash
scripts/delete-merged-branch.sh <branch> <pr-number>
```

It checks the PR is MERGED, updates `main`, then confirms every line the branch added is on `main` before
`git branch -D` (squash merges make `-d` refuse, and a plain diff against a `main` that has moved on gives false
alarms). If it reports lines not on `main`, stop and ask: the branch has work that did not reach `main`.

Small fixes that change no behaviour (typos, docs, tooling) may skip the spec: branch `chore/…` or `fix/…`, one PR.
Anything that changes what the API returns, what is stored, or what a model is asked is a feature and needs a spec.

`main` is protected twice: a GitHub ruleset (`scripts/protect-main.ps1`) and the PreToolUse hook in
`.claude/settings.json`, which refuses `git commit`/`git push` on `main`, pushes to `main`, and `gh pr merge`.
If the hook blocks you, create the right branch; never work around it.

## Fixed decisions (summary; details and reasons in `docs/plan.md`)

| Area | Rule |
|------|------|
| Runtime | C# on .NET 10, ASP.NET Core minimal APIs. Solution file `RegLens.slnx` |
| Chat models | `claude-sonnet-5-5` for answers; `claude-haiku-5-5` for chunk context (D50); `claude-haiku-4-5` as the eval judge. Via the official `Anthropic` NuGet package's `AsIChatClient(model)` behind `Microsoft.Extensions.AI.IChatClient` |
| Chunk context | Spec 0005: Anthropic's cookbook prompt (`ContextPrompt`), document block cached (5-minute `WithCacheControl`), first chunk alone then `Context:Concurrency`. Stored in `chunk_contexts` by input hash. Used for embedding and BM25 only, **never sent to the answer model** (D52). Bump `DocumentChunker.Version` / `ContextPrompt.Version` whenever chunks or contexts would change: `documents.pipeline` makes stale documents re-ingest |
| Embeddings | Local Ollama `nomic-embed-text` (768 dims) via OllamaSharp's `IEmbeddingGenerator`. Not `Microsoft.Extensions.AI.Ollama` (deprecated). Anthropic has no embedding model. Check the model card for the document/query task prefixes |
| Store | Postgres 17 + pgvector, one `chunks` table: `vector(768)` with HNSW `vector_cosine_ops`, plus a generated `tsvector` with GIN |
| Retrieval | Dense top-40 + keyword top-40, fused with RRF (k = 60), top-8. Keyword side ranks by **BM25 computed in our SQL** (D43, D49; migration 003 statistics, refreshed on ingest); `ts_rank_cd` remains as an eval configuration and is **not BM25** |
| Answers | Retrieved text is **data, never instructions** (OWASP LLM01). Structured output `{answer, citations[{chunkId, quote}], abstained, missingInfo}`. A code verifier checks every cited id was retrieved and every quote is in that chunk (normalised substring); failed citations are dropped, none left → abstain. Claude decides abstention; a similarity floor skips the call for unrelated questions. Not Claude's built-in Citations (incompatible with structured outputs). `POST /ask`, HybridCache + Redis |
| MCP | `ModelContextProtocol.AspNetCore` 2.4.0, stateless Streamable HTTP at `/mcp` (spec 0006). Read-only tools `search_regulations`, `get_passage`, `ask_regulations` in `RegulationTools`, sharing `RequestValidation` and the use cases with REST; annotated `ReadOnly`, structured output; errors as `McpException`. A new endpoint is protected by the fallback policy unless it says `AllowAnonymous` |
| Ops | Spec 0006. OpenTelemetry from API and Ingest → standalone Aspire dashboard (UI 18888, OTLP 4317), prompt/answer text included (D62). API key in `X-API-Key`, from `Auth:ApiKey` (env var or user-secrets, never appsettings), constant-time compare (D58). Rate limits per caller, shared by REST and MCP: ask 5/min, read 30/min (D59). `/health/live` + `/health/ready` (Postgres, Ollama; Redis = Degraded) (D61). API connects as `reglens_api` in role `reglens_readonly`, SELECT only (D60). `AddStandardResilienceHandler` on outbound HTTP |
| Evals | `evals/golden-set.json` (30: 20 answerable with expected doc + references, 10 not), owner-reviewed. `src/RegLens.Evals` console app, never in `dotnet test`; paid runs need `--confirm-spend`. recall@5/@8, MRR@10, abstention accuracy, citation rate, groundedness/relevance (Haiku judge with a TopP-dropping wrapper, 10 hand-checked). Results in `evals/results.md` with n, date, model IDs, commit. Never report a number without n |

## Corpus rules

- `corpus/` holds the official PDFs and `corpus/manifest.json` (source page + URL, version, download date, bytes,
  pages, SHA-256). Adding, replacing or removing a document is a change to the manifest in the same PR.
- Scope is **NBFCs**: RBI (NBFCs – KYC) Directions 2025, RBI (NBFCs – Credit Facilities) Directions 2025
  (Chapter III = Digital Lending) and RBI (NBFCs – Responsible Business Conduct) Directions 2025 (KFS, penal
  charges, recovery; D41), plus DPDP Rules 2025 (G.S.R. 846(E)) and its corrigendum (G.S.R. 892(E)).
- RBI repealed the 2016 KYC Master Direction and the standalone Digital Lending Directions on 28 Nov 2025. Never cite
  or ingest them.
- Ingest only the manifest's `ingest_pages` where given (DPDP Rules: English pages 24–41). Hindi text extracts as
  garbage.
- `rbidocs.rbi.org.in` blocks scripted downloads. Get RBI files through the owner's browser, with the owner's go-ahead.
- RBI updates directions in place: check the "Updated as on" date before trusting a file.

## Open questions

See `docs/plan.md` §3. Ask before building the part each one blocks.
