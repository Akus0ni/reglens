# Project log

Newest first. What was done, why, and the problems hit.

## 2026-11-24 — Portfolio pass: ADRs and the scale-up design

- Three ADRs (hybrid search, the MCP boundary, citation verification) and a one-page scale-up design with a cost model
  from measured numbers: $0.0122 per model-calling answer (3,896 tokens in, 444 out, n = 29), $0.00054 per chunk
  context, $0.045 per agent review. The README links them and the agent repo.

## 2026-11-24 — Fix: MCP results now match their output schema

- The Day 2 agent's first live run failed on its first tool call: the Python MCP client validates `structuredContent`
  against the tool's `outputSchema`, and RegLens left out null properties (`degraded`, `abstainReason`) that the
  schema marks required. The C# SDK omits nulls by default; the C# client doesn't validate, so spec 0006's tests
  passed. Tools now serialize nulls; a new test checks each tool's result against the required properties of its
  schema (it fails without the fix on `$.degraded` and `$.abstainReason`). Tests: 288.

## 2026-11-24 — Day 1 shipped: README

- `README.md`: problem, corpus, architecture diagram, results with n and dates, Windows quickstart, MCP and traces,
  production basics, known limitations, and the spec index. The sprint's "Ship Day 1" step had been missed.
- Day 2 decisions recorded: public repo `reglens-agent` (D65), OpenTelemetry to Aspire instead of Langfuse (D66), the
  same spec-first workflow (D67), $3 cap (D68).

## 2026-11-24 — Spec 0006: MCP server, tracing and production basics built

- `/mcp` (MCP C# SDK 2.4.0, stateless) with `search_regulations`, `get_passage` and `ask_regulations`, plus
  `GET /passages/{id}`. The SDK's own client, in tests and from `scripts/mcp-check.cs`, connects with protocol
  `2026-07-28` and with `2025-11-25`.
- API key (`X-API-Key`, fail closed), rate limits shared by REST and MCP (ask 5/min, read 30/min), `/health/live` and
  `/health/ready`, loopback-only `AllowedHosts`, OpenTelemetry from API and Ingest to the Aspire dashboard, and a
  SELECT-only `reglens_api` login (migration 005 + `scripts/create-api-db-user.ps1`).
- One `/ask` traced end to end: 3.49 s, of which Sonnet 2.84 s, embedding 0.14 s, SQL about 50 ms.
- Problems hit: HybridCache runs its factory without the caller's ExecutionContext, so answer spans became separate
  traces until the parent was passed in; `Host=localhost` cost about 2 s per new Postgres connection on Windows (IPv6
  first), which failed the 2 s readiness check, so connection strings now use 127.0.0.1; the Ingest host never
  started, so OpenTelemetry never ran there; PowerShell 5.1 has no `RandomNumberGenerator.Fill` (the first generated
  password was all zero bytes; regenerated with `GetBytes`).
- Spend: about $0.02 of the $0.50 cap. Tests: 285.

## 2026-11-24 — Spec 0005 follow-ups

- A5: checked every chunk context that names a chapter. Two DPDP contexts (Rules 7 and 14) invent chapters; the corrigendum
  dates and all RBI chapter references are right. Accepted as a known limitation (D57).
- hybrid-bm25 stays the default although dense now scores higher on n = 20 (D56).
- `scripts/delete-merged-branch.sh` sets `MSYS_NO_PATHCONV=1`, so Git Bash no longer rewrites `main:.claude/…` arguments
  as Windows paths.

## 2026-11-24 — Spec 0005: contextual retrieval and heading fixes built

- Chunker 2 fixes H1–H5 (section letters A–Z, wrapped headings, contents-page sections, 105A / 11A). A re-chunk
  diff against the stored chunks shows exactly the 13 expected changes (513 → 512 chunks). Migration 004 adds
  `chunk_contexts` and `documents.pipeline`; a pipeline change re-ingests without `--force`.
- `claude-haiku-5-5` (effort low, cookbook prompt) wrote 512 contexts in 208 s for **$0.2746**: one cache write per
  document, 98% of input tokens read from the prompt cache. A second ingest made no API call; a forced document
  rebuilt from cached contexts for $0.
- **Retrieval** (n = 20): the heading fixes alone changed nothing; with context, hybrid-bm25 recall@5 0.85 → 0.90,
  recall@8 0.85 → 0.95, MRR@10 0.772 → 0.907. rbc-01 none → rank 1, dpdp-01 10 → 7, cf-05 none → 17 (still a miss).
  Dense alone is now best (0.95 / 1.00 / 0.931). Floor 0.60 → 0.63.
- **Answers** (n = 30, prompt v3 with headings): 10/10 unanswerable abstained, 0/20 answerable wrongly abstained
  (was 2/20), 19/20 cited an expected paragraph; judge groundedness 4.90, relevance 4.45 (n = 20); $0.557. The rbc-03
  hours question now separates Para 100 (8 a.m.–7 p.m.) from microfinance Para 91 (9 a.m.–6 p.m.).
- Spend: about $0.84 of the $2 cap.
- Problems hit: `IChatClient` usage puts cache writes only in `AdditionalCounts` (measured with a 2-call probe);
  `GetCacheControl` is internal despite its XML docs; an SDK timeout looks like a cancellation; Python on Windows
  rewrote `\n` escapes during an edit (caught by review); `scripts/delete-merged-branch.sh` breaks in Git Bash on
  `branch:.dir/file` arguments (MSYS path conversion; fix with `MSYS_NO_PATHCONV=1`, not in this PR).
- Tests: 235.

## 2026-11-24 — Spec 0005 drafted: contextual retrieval and heading fixes

- PRs #13 and #14 merged. Owner decisions D50–D55: Haiku 5.5 writes chunk context; heading fixes and headings in the
  answer prompt join this spec; context for retrieval only; replace in place; a failure fails the document with
  resumable cached contexts; $2 cap.
- Found while writing it: `claude-haiku-5-5` (2026-10-07) is live and ten times cheaper than Haiku 4.5, with thinking
  on by default (so the spec sets effort low). Its tokenizer counts this corpus about 1.4× higher (exact counts via
  `count_tokens`). An unchanged PDF is always skipped on ingest, so a pipeline version is needed for new context to
  apply at all. Five structure defects, beyond the one the judge check found: RBC section I, a three-line
  section-D heading, the enacting formula filed under a contents line, and the inserted paragraphs 105A (RBC) and
  11A (KYC Annex I).

## 2026-11-24 — Spec 0004: judge hand check (A6)

- The owner delegated the 10-judgement hand check to Claude (`claude-opus-5-5`). Agreement with the Haiku judge:
  10/10 within 1 point, 8/10 exact. This is a second-model check, not a human one: all three models are Claude,
  and the judge's scores were visible. Rubrics were read from the evaluator package so both use the same scale.
- Found while checking rbc-03: the chunker misses the two-line heading "I. Responsibilities of … Recovery Agents"
  (the section regex accepts A–H only), so RBC Paras 98–101 are labelled as microfinance. The answer prompt sends no
  headings, so the model could not tell Para 91 (microfinance, 9 a.m.–6 p.m.) from Para 100 (other loans,
  8 a.m.–7 p.m.). Both need a spec.
- `evals/results.md` and `evals/judge-check.md` are generated; re-running `report` or `judge-sheet` overwrites the
  hand-added agreement and scores.

## 2026-11-24 — Spec 0004: golden set, evals and BM25 built

- New corpus document ingested (124 chunks). The annex rule now accepts "Annex I – <title>" on one line; the other
  four documents re-ingested hash-identical. Migration 003: BM25 statistics as materialized views, refreshed on
  ingest. `src/RegLens.Evals` runs retrieval and answer evals, the floor, the injection probe and the report.
- **Retrieval** (n = 20): hybrid-bm25 recall@5 0.85, recall@8 0.85, MRR@10 0.772 vs hybrid-tsrank 0.80 / 0.85 /
  0.637; dense MRR 0.788. Spec 0002's A5 question went from outside the top 20 to rank 3. The owner chose BM25 as
  the default (D49). Floor from the golden set: 0.60.
- **Answers** (n = 30, Sonnet 5.5 medium): 10/10 unanswerable abstained, 2/20 answerable wrongly abstained (both
  retrieval misses), 17/18 answered cited an expected paragraph, 0 citations dropped; judge groundedness 4.89 and
  relevance 4.33 (n = 18), owner hand-check pending. Injection probe passed. About $0.55 spent.
- Problems hit: PostgreSQL 17's safe `search_path` broke `ts_stat` inside a materialized view (schema-qualified);
  guessed judge token keys recorded no judge cost for the run (fixed, reported as "not recorded"); the first
  hand-check sheet sampled only KYC (now spread); a floor rounding bug; a timing-dependent Redis test.
- Tests: 196.

## 2026-11-23 — Spec 0004 drafted: golden set, evals, BM25

- PR #10 merged. Q7 answered yes: RBI (NBFCs – Responsible Business Conduct) Directions 2025 (id 12942, updated as on
  1 July 2026) downloaded through the owner's Chrome (495,859 bytes, 36 pages, Annex I = KFS format inside).
- Owner decisions D41–D48: golden set drafted by Claude and reviewed row by row; BM25 in SQL; contextual retrieval
  split into spec 0005 (MCP → 0006); console eval runner; medium effort only; Haiku judge hand-checked; floor from
  the golden set.
- Found while checking sources: Microsoft's quality evaluators send `temperature` and `top_p` together, which Claude
  4.x rejects; the judge gets a wrapper that drops `top_p`.

## 2026-11-23 — Spec 0003: answers with verified citations built

- `POST /ask`: search → similarity floor → Sonnet 5.5 with passages as escaped data and a JSON schema → our
  verifier drops any citation whose chunk was not sent or whose quote is not in it → answer or abstention.
  HybridCache with Redis (added to compose); refusals and degraded answers are not cached. `/search` hits gained
  `similarity`.
- **Results** (spec 0003 §10, n = 9, `claude-sonnet-5-5`, effort medium): 4 of 5 planned answerable questions
  answered with verified citations, 0 citations dropped; the fifth (KFS contents) abstained correctly because the
  KFS rules live in the RBI Responsible Business Conduct Directions, which are not in the corpus (new Q7).
  Out-of-scope questions abstained (2 by Claude, 1 by the floor with no call). **$0.0128 per Claude call**,
  p50 3.3 s; cache hit 29–44 ms; 10 concurrent identical questions → 1 call.
- Problems hit: an answer mentioned "Passage 184" → prompt v2. With Redis down, StackExchange.Redis's 5 s default
  timeouts added 5–9 s per request → 500 ms timeouts, ~0.9 s. `/ask` with Ollama down takes 30 s (Ollama retries
  before the keyword fallback): follow-up. `GetResponseAsync<T>` is in `Microsoft.Extensions.AI`, not the
  abstractions, so the schema is sent and parsed by our code.
- Tests: 163 (55 new).

## 2026-11-23 — Spec 0003 drafted: answers with verified citations

- PR #8 merged; the owner accepted spec 0002's A5 gap and moved IDF weighting to spec 0004 (D40).
- Checked before writing: Claude's built-in Citations cannot be combined with structured outputs (400), so answers use
  a JSON schema plus our own verifier (D32). Best cosine similarity for 5 in-scope and 5 out-of-scope questions
  overlaps (0.630–0.850 vs 0.536–0.716, n = 10), so Claude decides abstention and a 0.55 floor only skips the call for
  clearly unrelated questions (D33).
- Owner decisions D32–D40: drop failed citations, `POST /ask`, effort medium, HybridCache + Redis, answer when
  degraded, normalised quote matching.

## 2026-11-23 — Spec 0002: hybrid retrieval built

- `RegLens.Api` with `GET /search`, migration 002 (heading + para_ref in `tsv`), citation lookup, keyword-only
  fallback, model warm-up. Port moved to 5180 (D31): the owner's AgencyOS.Server holds 5080.
- **Results** (spec 0002 §10): A3, A4, A6, A7, A8 pass. **A5 fails**: Credit Facilities Para 13 is dense rank 3 but
  fused rank 21, because OR-matched common words put other chunks in both lists and `ts_rank_cd` has no IDF.
  Left to the owner; candidate fix is IDF weighting, evaluated in spec 0004.
- Latency, 20 warm searches: p50 66 ms, p95 78 ms. Degraded search (Ollama unreachable): 12.3 s, from retries.
- Problems hit: `topK=abc` returned 500 in Development (binding exception through the exception handler); fixed with
  `ThrowOnBadRequest = false`. The cleanup script was O(lines) `git show` calls; fixed in #6.
- Tests: 108 (42 new).

## 2026-11-23 — Spec 0001: ingestion built

- Clean-architecture solution (D22), InvoiceGen conventions (D25), `docker-compose.yml` (D26), versioned SQL
  migrations (D24), console host (D23; the API trigger comes with spec 0005, P1).
- **Measurements** (corpus manifest checked 2026-11-23; Ollama `nomic-embed-text:latest`, digest `0a109f422b47`,
  context 2048; full run with the model already loaded):

  | Document | Chunks | Tokens min / median / max | Time |
  |----------|--------|---------------------------|------|
  | rbi-nbfc-kyc-2025 | 167 | 14 / 300 / 500 | 16.9 s |
  | rbi-nbfc-credit-facilities-2025 | 131 | 3 / 106 / 490 | 10.5 s |
  | dpdp-rules-2025 (pages 24–41) | 90 | 13 / 110 / 496 | 2.0 s |
  | dpdp-rules-2025-corrigendum | 1 | 333 | 0.1 s |
  | **Total** | **389** | | **29.5 s** |

- A7: second run skipped all four documents; `chunks` stayed at 389. A9: every vector has 768 dimensions.
- A8 (automated part): for 10 random chunks (`setseed(0.42)`), the text was found on the recorded pages and the
  reference matches the paragraph number: 10/10. The owner's reading check is pending in the PR.
- Problems hit: the first embedding call after Ollama unloads the model took 32.3 s (later calls ≈ 0.1 s), so the
  standard 10 s attempt timeout failed every cold start; now 60 s and configurable. The real corpus exposed
  table-of-contents leftovers, wrapped titles and schedule captions becoming tiny chunks; fixed with ordered
  chapters and intro folding (spec 0001 §9). Old `reglens-pg` / `aspire-dashboard` containers from the tracker
  held the ports; removed with the owner's go-ahead.
- Tests: 66 (41 Domain, 10 Application, 12 Infrastructure on a pgvector Testcontainer, 3 architecture).

## 2026-11-23 — Branch cleanup

- PR #1 merged (squash, b070fbe). Turned on the repo setting `delete_branch_on_merge`. The local branch needed
  `git branch -D` because a squash merge creates a new commit; the skill now checks `git diff --stat main <branch>`
  is empty before deleting (D21).

## 2026-11-23 — Repo workflow

- Added `CLAUDE.md`, the `reglens-spec` skill with a spec template, `docs/plan.md`, this log, `CODEOWNERS`, a PR
  template, `scripts/protect-main.ps1` and a Claude Code PreToolUse hook (`.claude/hooks/guard-main.mjs`).
- Decided: spec PR first, then code PR; only the owner merges; `main` takes changes only through PRs (D17–D20).
- The hook was tested against simulated tool calls: commit/push on `main`, pushes to `main` from another branch and
  `gh pr merge` are blocked; normal branch work passes. `git branch --show-current` is used because
  `rev-parse --abbrev-ref HEAD` fails on a repo with no commits.

## 2026-11-23 — Corpus

- The tracker's corpus links were out of date. RBI consolidated its directions by entity type on 28 Nov 2025 and
  repealed the 2016 KYC Master Direction (the tracker linked its 2018 version) and the standalone Digital Lending
  Directions 2025. The owner chose the NBFC set (D6).
- The DPDP link in the tracker was PIB's 7-page press explainer, not the Rules. The notified Rules (G.S.R. 846(E))
  and corrigendum (G.S.R. 892(E)) came from MeitY instead (D7). The Rules PDF is bilingual: English is pages 24–41.
- `rbidocs.rbi.org.in` answers scripted requests with a JavaScript bot check. A headless-browser workaround was
  refused by the permission system; the owner had the two RBI PDFs downloaded through their own Chrome instead.
- Committed the four PDFs with `corpus/manifest.json` (13e7d5a).

## 2026-11-23 — Repo created

- Public repo `Akus0ni/reglens` with the .NET `.gitignore` and an empty `RegLens.slnx` (e254ab2).
