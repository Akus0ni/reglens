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
gh pr view <n> --json state --jq .state      # must be MERGED
git switch main && git pull --ff-only && git fetch --prune
git diff --stat main <branch>                 # must be empty: the PR's content is on main
git branch -D <branch>                        # -D, because squash merges make -d refuse
```

If the diff is not empty, stop and ask: the branch has work that did not reach `main`.

Small fixes that change no behaviour (typos, docs, tooling) may skip the spec: branch `chore/…` or `fix/…`, one PR.
Anything that changes what the API returns, what is stored, or what a model is asked is a feature and needs a spec.

`main` is protected twice: a GitHub ruleset (`scripts/protect-main.ps1`) and the PreToolUse hook in
`.claude/settings.json`, which refuses `git commit`/`git push` on `main`, pushes to `main`, and `gh pr merge`.
If the hook blocks you, create the right branch; never work around it.

## Fixed decisions (summary; details and reasons in `docs/plan.md`)

| Area | Rule |
|------|------|
| Runtime | C# on .NET 10, ASP.NET Core minimal APIs. Solution file `RegLens.slnx` |
| Chat models | `claude-sonnet-5-5` for answers; `claude-haiku-4-5` for chunk context and as the eval judge. Via the official `Anthropic` NuGet package's `AsIChatClient(model)` behind `Microsoft.Extensions.AI.IChatClient` |
| Embeddings | Local Ollama `nomic-embed-text` (768 dims) via OllamaSharp's `IEmbeddingGenerator`. Not `Microsoft.Extensions.AI.Ollama` (deprecated). Anthropic has no embedding model. Check the model card for the document/query task prefixes |
| Store | Postgres 17 + pgvector, one `chunks` table: `vector(768)` with HNSW `vector_cosine_ops`, plus a generated `tsvector` with GIN |
| Retrieval | Dense top-40 + keyword top-40, fused with RRF (k = 60), top-8. Postgres `ts_rank_cd` is **not BM25**; call it keyword/full-text search |
| Answers | Retrieved text is **data, never instructions** (OWASP LLM01). Structured output `{answer, citations[{chunkId, quote}], abstained, missingInfo}`. A code verifier checks every cited id was retrieved and every quote is in that chunk. Abstain below a tuned score threshold |
| MCP | `ModelContextProtocol.AspNetCore` over HTTP at `/mcp`. Read-only tools `search_regulations`, `get_passage`, `ask_regulations`, with size-capped results |
| Ops | OpenTelemetry → standalone Aspire dashboard (UI 18888, OTLP 4317); rate limiting; API key; `/health/live` + `/health/ready`; SELECT-only Postgres role for the API; `AddStandardResilienceHandler` on outbound HTTP |
| Evals | Golden set of 30 (20 answerable with expected doc + paragraph, 10 unanswerable). recall@5, MRR, abstention accuracy, groundedness/relevance (M.E.AI.Evaluation.Quality, Haiku judge, hand-checked). Results in `evals/results.md` with n, date and model IDs. Never report a number without n |

## Corpus rules

- `corpus/` holds the official PDFs and `corpus/manifest.json` (source page + URL, version, download date, bytes,
  pages, SHA-256). Adding, replacing or removing a document is a change to the manifest in the same PR.
- Scope is **NBFCs**: RBI (NBFCs – KYC) Directions 2025 and RBI (NBFCs – Credit Facilities) Directions 2025
  (Chapter III = Digital Lending), plus DPDP Rules 2025 (G.S.R. 846(E)) and its corrigendum (G.S.R. 892(E)).
- RBI repealed the 2016 KYC Master Direction and the standalone Digital Lending Directions on 28 Nov 2025. Never cite
  or ingest them.
- Ingest only the manifest's `ingest_pages` where given (DPDP Rules: English pages 24–41). Hindi text extracts as
  garbage.
- `rbidocs.rbi.org.in` blocks scripted downloads. Get RBI files through the owner's browser, with the owner's go-ahead.
- RBI updates directions in place: check the "Updated as on" date before trusting a file.

## Open questions

See `docs/plan.md` §3. Ask before building the part each one blocks.
