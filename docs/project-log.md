# Project log

Newest first. What was done, why, and the problems hit.

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
