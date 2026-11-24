# ADR 0003 — Citations are verified in code, never by a model

Status: Accepted (D13; specs 0003 in RegLens and 0001 in reglens-agent) · Date: 2026-11-24

## Context

A regulatory answer that cites the wrong paragraph, or a paragraph that was never retrieved, is worse than no answer.
Asking a model whether its own citations are real just moves the trust problem.

## Decision

- **RegLens answers:** Claude returns structured output (`answer`, `citations[{chunkId, quote}]`, `abstained`,
  `missingInfo`). `CitationVerifier` (Domain, pure code) keeps a citation only if its chunk was among the passages sent
  for this question and its quote is in that chunk (normalised substring, at least 20 characters). If no citation
  survives, the answer becomes an abstention (`unverified`).
- **The agent's findings:** every evidence id must have been returned by a tool in the same review, and the finding's
  paragraph reference must match one of those passages. Ids come only from the tools' structured content, never from
  model text. Failing findings are listed separately and never filed.

## Evidence

- RegLens answer eval (n = 30): citations dropped by the verifier 0; answerable questions answered with an expected
  paragraph 19/20; unanswerable questions abstained 10/10 (`evals/results.md`).
- Agent evals (spec 0002): "verified" held in 15/15 scenarios in both live runs. In a demonstration, the assess prompt
  told the model to cite "from memory" with invented passage ids; Sonnet 5.5 still cited only retrieved passages, so
  the verifier rejected nothing, but the broken prompt lost a required citation and the CI gate failed on it.

## Consequences

- A forged or mistyped citation cannot reach a user or a filed report.
- Code can only check that a quote exists, not that the claim around it is right. Whether a finding is supported by
  its passage is measured separately by an LLM judge with a hand check (agent judge: grounded 4.60/5, n = 30; hand
  check 20/20 within one point).
- Not using Anthropic's built-in Citations feature, which is incompatible with structured outputs.
