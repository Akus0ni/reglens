# ADR 0001 — Hybrid search with RRF as the default retrieval

Status: Accepted (D11, D43, D49; reaffirmed by D56) · Date: 2026-11-24

## Context

RegLens answers questions about regulation where users often name the exact provision ("Rule 7", "Para 29") and
just as often paraphrase it ("when must a breach be reported"). Dense vectors match meaning but rank exact identifiers
poorly; keyword search matches identifiers and rare terms but misses paraphrases. Their scores are on different scales,
so they cannot simply be added.

## Decision

Retrieve the top 40 by pgvector cosine similarity and the top 40 by keyword score, and fuse them with Reciprocal Rank
Fusion (k = 60), keeping the top 8. The keyword side ranks by BM25 computed in SQL from materialized statistics, not
Postgres `ts_rank_cd`. Citations written in the question are looked up exactly and pinned first.

## Evidence

Golden set, n = 20 answerable questions (`evals/results.md`):

| Configuration | recall@5 stage 1 | recall@5 stage 3 | MRR@10 stage 3 |
|---------------|------------------|------------------|----------------|
| dense | 0.80 | 0.95 | 0.931 |
| keyword BM25 | 0.80 | 0.85 | 0.768 |
| hybrid ts_rank | 0.80 | 0.90 | 0.760 |
| **hybrid BM25 (default)** | **0.85** | **0.90** | **0.907** |

Stage 1 is the first index; stage 3 adds Haiku-written chunk context (spec 0005). BM25 beat `ts_rank_cd` by 0.135 MRR
on the hybrid configuration at stage 1.

## Consequences

- At stage 3, dense alone scores one question higher at recall@5. With n = 20 that is not evidence, and the keyword
  side is what finds exact identifiers and G.S.R. numbers that dense search can rank low. The default stays hybrid
  until a larger golden set says otherwise (D56).
- Two retrieval paths to maintain, plus BM25 statistics that ingestion must refresh.
- The ranks behind every hit (dense, keyword, fused) are returned, so a bad result can be explained.

## Alternatives considered

- **Dense only:** simplest, and currently best on n = 20, but blind to exact references.
- **Score blending (weighted sum):** needs score normalisation that RRF avoids.
- **A reranker model:** another model call per question; not needed at this corpus size.
