# Project log

Newest first. What was done, why, and the problems hit.

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
