# Spec 0005 — Contextual retrieval and heading fixes

> Status: **Approved (PR #15, 2026-11-24) · Implemented in `feat/0005-contextual-retrieval`** · Created: 2026-11-24 · Owner: @Akus0ni
> Depends on: specs 0001–0004 · Blocked by open questions: none
> Sprint phase: D1·5

## 1. Context

A chunk is embedded and keyword-indexed on its own text. A paragraph that says "the LSP shall…" or "the KFS shall
contain…" does not say which document, chapter or subject it belongs to. Anthropic's contextual retrieval asks a
small model to write 50–100 tokens situating each chunk within its whole document. That text is prepended before
embedding and before keyword indexing. Anthropic reports that this cut the top-20 retrieval failure rate by 35%
(embeddings only) and by 49% (embeddings plus BM25) on their datasets (§9). Spec 0004 left three retrieval misses
in our golden set:

- **cf-05:** "LSP" against "lending service provider".
- **rbc-01:** Key Fact Statement contents, Para 29 / Annex I.
- **dpdp-01:** a paraphrase of "personal data breach".

Spec 0004's hybrid-bm25 run is the baseline: recall@5 0.85, recall@8 0.85, MRR@10 0.772, n = 20.

The judge check of spec 0004 found that the chunker gets some headings wrong in the Responsible Business Conduct
Directions (RBC) and that the answer prompt sends no headings at all. In rbc-03 the model could not tell that
Para 91 (calls only between 9 a.m. and 6 p.m.) is the microfinance rule and Para 100 (8 a.m. to 7 p.m.) covers other
loans, because the wrong heading was stored and none was sent. A heading path is also good input for the context
model, so both fixes are in this spec (D51).

Checked while writing this spec (`pdftotext` against the stored chunks, 2026-11-24). These are the structure
defects:

| # | Where | What the PDF has | What is stored today |
|---|-------|------------------|----------------------|
| H1 | RBC p. 27 | Section "I. Responsibilities of Direct Sales Agents (DSA) / Direct Marketing Agents (DMA) / Recovery Agents of the NBFC" (two lines), Paras 98–101 | Not recognised (the section rule accepts A–H only); Paras 98–101 carry "H.5 Engagement of recovery agents for recovery of microfinance loans" |
| H2 | RBC p. 30 | Section "D. Compliance with Hon'ble Supreme Court Order dated April 30, 2025 in the matter of Pragya Prasun … (W.P.(C) 49 of 2025)" (three lines) | Heading cut after "in the"; the rest becomes a chunk whose `para_ref` is the heading |
| H3 | RBC p. 2 | Table of contents, then the enacting formula ("In exercise of the powers conferred by sections 45JA…") | The last contents line is taken as a section heading; the enacting formula is stored under `para_ref` "D. Compliance …" |
| H4 | RBC p. 31 | Paragraph "105A." (inserted by amendment) | Not recognised as a paragraph; its chunk has `para_ref` "E. Measures in case of declaration of calamity" |
| H5 | KYC Annex I | Paragraph "11A." | Merged into "Annex I, Para 11, part 2 of 2" |

## 2. Decisions

Builds on: D3 (Haiku for chunk context), D16 (contextual retrieval as an eval configuration), D36 (effort as a
setting), D43/D49 (BM25 default), D44 (this spec), D48 (floor rule).

New, decided by the owner on 2026-11-24:

| # | Decision |
|---|----------|
| D50 | **Chunk context is written by `claude-haiku-5-5`**, not `claude-haiku-4-5` (amends D3 for chunk context only; the eval judge stays `claude-haiku-4-5` so judge scores stay comparable with spec 0004) |
| D51 | **Headings are fixed in this spec.** The chunker fixes H1–H5. The context model gets each chunk's heading path. The answer prompt gets each passage's heading (prompt v3) |
| D52 | **Generated context is used for retrieval only** (embedding and BM25). It is never sent to the answer model; answers and quote verification use the regulation text alone |
| D53 | **Replace in place:** every chunk is contextualized and re-embedded. Results are compared with spec 0004's saved runs. A setting rebuilds without context (local re-embed, no API cost) |
| D54 | **A context failure fails the document:** its old chunks stay. Generated contexts are saved as they arrive, so a re-run makes API calls only for chunks still missing a context |
| D55 | **Spend cap for this spec's paid runs: $2** |

Proposed by this spec — **confirm by approving the spec PR**, or say which to change:

| # | Proposal | Why |
|---|----------|-----|
| P1 | **Context prompt v1 = Anthropic's cookbook prompt** (verbatim, §5), with the chunk wrapped as `<chunk heading="…" ref="…">`. The document goes in the first user content block with `cache_control` (5-minute TTL), and the chunk goes in a second block. No domain tweaks such as abbreviation expansion | Tuning the prompt on 20 golden questions would overfit them. Anthropic's own prompt is the honest first measurement |
| P2 | **"Whole document" = the document's extracted text over its `ingest_pages`** (the same text the chunker reads), joined page by page. It is identical for every chunk of a document, which is what lets the cache hit | Any byte difference in the cached block is a cache miss |
| P3 | **Context call settings:** `claude-haiku-5-5`, effort **low**, `MaxOutputTokens` 1024. No temperature, top_p, top_k or prefill (Haiku 5.5 rejects or discourages them). The text is read from text blocks only, ignoring thinking blocks | Thinking is on by default at medium effort, and thinking tokens count toward `max_tokens` and cost. Low effort "can skip thinking entirely on simpler requests" (§9) |
| P4 | **Order and concurrency:** documents one at a time. Within a document, the first chunk is sent alone, then the rest with `Context:Concurrency` parallel calls (default 4) | A cache entry exists only after the first response starts; parallel first calls would all write the cache |
| P5 | **Context cache table** `chunk_contexts(input_hash PK, model, prompt_version, context, input_tokens, cache_write_tokens, cache_read_tokens, output_tokens, created_at)`. `input_hash` = SHA-256 of prompt version, model, document text, heading, `para_ref` and chunk text. A row is written as soon as its context arrives, outside the document transaction | Makes a re-run resume (D54) and makes a rebuild with context free when nothing changed |
| P6 | **Pipeline version:** migration 004 adds `documents.pipeline` (e.g. `chunker 2; context claude-haiku-5-5 v1`, or `chunker 2; no context`). A stored document is **Outdated** when its file hash *or* pipeline differs, so changing the chunker, the context model or the prompt re-ingests without `--force` | Today an unchanged PDF is always skipped, so the new context would never be applied |
| P7 | **Embedding input** = `search_document: ` + context + blank line + chunk text (cookbook order). The keyword index already includes `context` (migration 002), so BM25 sees it with no schema change; BM25 statistics are refreshed after ingest as today | Matches the method being measured. A max of about 2,000 characters plus about 150 context tokens is far below nomic's 2,048-token context |
| P8 | **Setting `Context:Enabled`** (default `true` in the Ingest host and the API ingest trigger). When `false`, no Anthropic call is made, `context` is null, and the pipeline says `no context` | The D53 rebuild without context, and the free baseline run in P9 |
| P9 | **Three-stage measurement**, so the chunker fix and the context are measured apart: (1) spec 0004's saved runs; (2) after the chunker fix with `Context:Enabled=false`; (3) with context. All five retrieval configurations at stages 2 and 3 (free). One answer run (n = 30) at stage 3 with the judge, against spec 0004's answer run. The floor is recomputed with the D48 rule at stage 3 and applied | Otherwise a gain from the heading fixes would be credited to contextual retrieval |
| P10 | **Usage and cost are recorded per document:** input, cache-write, cache-read and output tokens and the cost at list price, in the ingest log and report. The `Pricing` table gains `claude-haiku-5-5` ($0.10 in / $0.125 5-minute cache write / $0.01 cache read / $0.50 out per MTok, prompts up to 100k tokens) | The D55 cap needs measured spend, not an estimate |
| P11 | **Answer prompt v3:** each `<passage>` gets a `heading` attribute (escaped like the other attributes). `StoredPassage` gains `Heading`. Prompt v3 is a new answer-cache key, so cached answers are recomputed | D51; rbc-03 needs it |
| P12 | **Chunker rules** for H1–H5: section letters A–Z; the next line joins a heading while the heading so far ends with "/", ends with one of the words "of", "in", "the", "and", "for", "to", "on", or has an unclosed "(", or while the next line starts with "/" or a lowercase letter, and is never a paragraph, chapter, section or annex start (H2's three lines: "…in the" / "matter of … (W.P.(C) 289 of" / "2024) and … 49 of 2025)"); no section heading counts before the first chapter heading (the table of contents); a paragraph number may carry one capital letter (`105A.`, giving `Para 105A`, which must follow `Para 105`). `NumberedStructureChunking`'s version becomes `chunker 2` | Each rule fixes one row of §1; the version feeds P6 |

## 3. Scope

**In scope:**
- The chunker fixes H1–H5, with tests for each.
- The context port and its Anthropic implementation, the context cache, and the pipeline version.
- Migration 004 and re-ingest of the whole corpus.
- Answer prompt v3 with headings.
- Retrieval evals at stages 2 and 3, one answer eval run, the floor update, and `evals/results.md`.

**Out of scope:**
- Reranking (Anthropic's 67% figure needs a reranker; not this spec).
- The Message Batches API (the whole run is about $0.25).
- Prompt variants beyond P1.
- Showing context in `GET /search` or `/ask`.
- Changes to the golden set.
- The Haiku 4.5 judge (D50 keeps it).
- MCP (spec 0006).

## 4. Requirements

| ID | Requirement |
|----|-------------|
| R5.1 | The chunker recognises section headings A–Z; RBC Paras 98–101 carry the heading path ending "I. Responsibilities of Direct Sales Agents (DSA) / Direct Marketing Agents (DMA) / Recovery Agents of the NBFC" (H1) |
| R5.2 | Wrapped heading lines are joined into the heading (P12); RBC section D's heading ends "(W.P.(C) 49 of 2025)" and no chunk has `para_ref` equal to a heading's text where a numbered paragraph follows it (H2) |
| R5.3 | Section headings before the first chapter heading are ignored; the RBC enacting formula is part of the Preamble chunk (H3) |
| R5.4 | A paragraph number with one capital-letter suffix starts a paragraph: RBC `Para 105A`, KYC `Annex I, Para 11A` (H4, H5) |
| R5.5 | Re-chunking KYC, Credit Facilities and DPDP changes only the chunks listed in §1; every other chunk keeps its `para_ref`, heading and text. RBC changes only H1–H4. Checked by a comparison against today's chunks |
| R5.6 | An `IChunkContextGenerator` port (Application) and an Anthropic implementation (Infrastructure) produce one context per chunk per P1–P4, through `IChatClient` with `WithCacheControl` on the document block |
| R5.7 | Contexts are cached in `chunk_contexts` by input hash (P5); a cached context is reused without an API call |
| R5.8 | If any chunk's context fails after the SDK's retries, the document is not replaced, the error names the document and chunk, and the contexts already generated stay cached (D54) |
| R5.9 | A context that is empty or whitespace after trimming is a failure; a context longer than 2,000 characters is a failure |
| R5.10 | Migration 004 adds `chunk_contexts` and `documents.pipeline`; `GetStateAsync` reports Outdated when the pipeline differs (P6) |
| R5.11 | Chunks are embedded from prefix + context + blank line + content when a context exists, and from prefix + content when it does not (P7) |
| R5.12 | `Context:Enabled=false` makes no Anthropic call and stores null contexts (P8) |
| R5.13 | The ingest log and report give, per document: chunks contextualized, chunks served from the context cache, the four token counts and the cost (P10) |
| R5.14 | The answer prompt is v3: each passage carries its heading (P11); quotes are still verified against `content` only |
| R5.15 | The eval runner records the pipeline (chunker and context versions) on every run, and `evals/results.md` shows the stage 1–3 comparison (P9) |
| R5.16 | The floor is recomputed with D48 at stage 3 and applied, with its numbers |
| R5.17 | Total measured spend for the spec's paid runs stays at or under $2 (D55); the runner refuses a paid run without `--confirm-spend` as today |

## 5. Design

```text
IngestCorpus (per document, if Missing or Outdated by sha256 + pipeline)
  ├─ textSource.ReadPages ──► DocumentChunker (chunker 2: H1–H5) ──► chunks with heading paths
  ├─ ChunkContextualizer (Application)                         [only if Context:Enabled]
  │    ├─ input_hash per chunk ──► chunk_contexts hit? reuse
  │    └─ miss ──► IChunkContextGenerator (Infrastructure, Haiku 5.5)
  │            first miss alone, then Concurrency-wide; save each row on arrival
  ├─ ChunkEmbedder: "search_document: " + context + "\n\n" + content
  └─ ReplaceDocumentAsync (one transaction: document row with pipeline, chunks with context)
  then RefreshSearchStatisticsAsync (BM25 views; tsv already includes context)
```

The context prompt (P1). The text is from Anthropic's contextual-embeddings cookbook, with the chunk tag carrying
our heading and reference:

```text
[user, block 1, cache_control ephemeral 5m]
<document>
{whole document text}
</document>

[user, block 2]
Here is the chunk we want to situate within the whole document
<chunk heading="{heading path}" ref="{para_ref}">
{chunk content}
</chunk>

Please give a short succinct context to situate this chunk within the overall document for the purposes of improving search retrieval of the chunk.
Answer only with the succinct context and nothing else.
```

Model calls and cost, from exact token counts. The documents were counted with the free `count_tokens` endpoint on
2026-11-24; Haiku 5.5's tokenizer gives about 1.4× Haiku 4.5's counts for this text:

| Document | Chunks | Document tokens (Haiku 5.5) |
|----------|--------|-----------------------------|
| KYC | 167 | 62,522 |
| Credit Facilities | 131 | 28,545 |
| RBC | 124 | 20,724 |
| DPDP Rules | 90 | 19,623 |
| DPDP corrigendum | 1 | 684 |

The estimate assumes one cache write per document, one cache read per further chunk, about 500 chunk and
instruction tokens in, and up to about 300 tokens out (context plus any thinking). That gives about
**$0.25 for 513 calls**, dominated by cache reads of the KYC document (166 × 62.5k × $0.01/MTok ≈ $0.10).

- Without caching, the same run would be about $1.20.
- Every prompt is under Haiku 5.5's 100k-token higher-price threshold.
- Every document is over its 512-token cache minimum.
- The answer run is about $0.35 (spec 0004 measured $0.332) and the judge about $0.2.
- **Spec total ≈ $0.8, cap $2.**

`IChatClient` with prompt caching is verified locally against the `Anthropic` 13.12.0 package's XML docs.
`AIContentCacheExtensions.WithCacheControl(content, Ttl?)` places the breakpoint at the end of the content block, and
it works only with the client returned by `AsIChatClient`. The code PR confirms the cache hit from the reported
usage (cache-read tokens > 0 from the second chunk on).

### System design concepts exercised

| Concept | Where | What could go wrong without it |
|---------|-------|--------------------------------|
| Prompt caching of a shared prefix | P1, P2, P4 | Paying for each 62k-token document 167 times (≈ 5× the cost) |
| Content-addressed cache, resumable batch job | P5, R5.7–R5.8 | A failure at chunk 150 pays for 149 contexts again |
| Pipeline versioning for derived data | P6 | Changing the chunker or prompt silently leaves old data in place |
| Ablation | P9 | Crediting the heading fixes' gain to contextual retrieval |
| Keeping model-written text out of answers | D52, R5.14 | Generated, non-regulatory text quoted as if it were regulation |
| Not tuning on the test set | P1 | A prompt that fits 20 golden questions and nothing else |

## 6. Acceptance criteria

| # | Check | How |
|---|-------|-----|
| A1 | Builds with zero warnings; all tests pass: chunker cases H1–H5; context prompt shape and cache-control placement (fake `IChatClient`); cache reuse and resume after failure; pipeline → Outdated; embedding input with and without context; prompt v3 heading attribute | `dotnet build`, `dotnet test` |
| A2 | Re-chunk comparison shows exactly the §1 changes (R5.5) | comparison script output in the PR |
| A3 | Full ingest with context: every chunk has a context; cache reads appear from the second chunk of each document; per-document tokens and cost are logged | ingest log |
| A4 | A second ingest with nothing changed makes no Anthropic call | ingest log |
| A5 | The owner reads 5 sampled contexts (one per document, plus one RBC chunk from section I) and finds them accurate | PR body |
| A6 | Retrieval stages 2 and 3, five configurations each, n = 20, in `evals/results.md` beside stage 1 | `… retrieval --config …` |
| A7 | One answer run at stage 3, n = 30, with judge scores, compared with spec 0004's run; the floor is recomputed and applied | `… answers --confirm-spend`, `… floor` |
| A8 | `POST /ask` for rbc-03 separates the microfinance hours (Para 91) from other loans (Para 100) | request in the PR body |
| A9 | Measured spend ≤ $2 | ingest report + run files |

## 7. Measurements

- recall@5, recall@8 and MRR@10 for five configurations at stages 2 and 3 (n = 20), beside spec 0004.
- The three 0004 misses (cf-05, rbc-01, dpdp-01) by rank at each stage.
- Answer metrics at stage 3 (n = 30) against spec 0004.
- Floor before and after.
- Context generation: calls, cache-hit share of input tokens, tokens, cost, wall time.

Every table gives the date, model IDs, pipeline and commit. With n = 20, a change of one question moves recall by
0.05: the spec reports per-question ranks so a change can be traced, and does not claim a percentage improvement
from so few questions.

## 8. Open questions

| # | Question | Blocks |
|---|----------|--------|
| — | None | — |

## 9. Changes made while building

| Where | Change | Why |
|-------|--------|-----|
| R5.6, P3 | How `IChatClient` reports usage was measured, not assumed (one 2-call probe, about $0.0006): `InputTokenCount` is all input, `CachedInputTokenCount` the cache reads, and cache writes appear only as `AdditionalCounts["CacheCreationInputTokens"]`. Uncached input = input − reads − writes. At effort low Haiku 5.5 returned no thinking | The XML docs do not say; P10 needs the split |
| R5.6, A1 | `AIContentCacheExtensions.GetCacheControl` is **internal** in 13.12.0 (reflection), although the XML docs list it. The test checks the public marker `AdditionalProperties["anthropic:cache_control"]` instead | Compile error |
| R5.8 | A model call that the SDK times out arrives as `OperationCanceledException` without the caller cancelling; it is wrapped as a context failure naming the chunk, like any other error | Otherwise the error would not name the document and chunk |
| P5 | `input_hash` = SHA-256 of prompt version, model, **SHA-256 of the document text**, heading, `para_ref` and chunk text, joined by NUL | Same content addressing as P5, without hashing 60k tokens 168 times |
| P1 | Chunk text goes in raw, as in the cookbook; only the `heading` and `ref` attribute values are escaped. The document block is raw too | Escaping one copy of the text and not the other would make them differ |
| P8 | There is no API ingest trigger (D23 was never built), so `Context:Enabled` lives in the Ingest host's settings only | Nothing to configure in the API |
| P10 | The price table moved from `RegLens.Evals` to `RegLens.Application` (`ModelPricing`, with cache write/read prices) so ingestion can report cost | Ingest cannot reference the eval runner |
| R5.15 | `results.md` groups runs by pipeline; runs saved before this spec count as `chunker 1; no context`. The 0004 hand-check row moved to `evals/hand-checks.json` (a regenerated report used to drop it) and the filled 0004 sheet to `evals/judge-check-0004.md` (the stage-3 answer run would have overwritten it). The runner refuses an index that mixes pipelines | Keep the record intact |
| A1 | H1–H5 test fixtures lower paragraph and chapter numbers (97 → 1, IV → III): a first paragraph must be within `MaxNumberGap` (3) of 0 | Otherwise the fixtures test the gap rule, not the fix |
| A2 | `scripts/chunk-diff.cs` (a .NET 10 file-based app) re-chunks the PDFs with no database or model and diffs against an export of the stored chunks | The comparison R5.5 asks for |

## 10. Results (2026-11-24; 5 documents, 512 chunks; `nomic-embed-text:latest`)

**A2, re-chunk comparison** (chunker 1 → 2, `scripts/chunk-diff.cs`): exactly the §1 changes, 13 in all, 513 → 512 chunks. Credit
Facilities and DPDP Rules: no change. KYC: `Annex I, Para 11A` added and Para 11 part 2 shortened (H5). RBC: Paras 98–101
re-headed to section I and Para 97 loses the heading text it had swallowed (H1); section D's heading is complete and its
orphan chunk is gone (H2); the TOC-titled chunk is gone and the Preamble takes the enacting formula (H3); `Para 105A` replaces
the chunk titled "E. Measures …" (H4).

**A3, context generation** (`claude-haiku-5-5`, effort low, prompt v1; 208 s, concurrency 4):

| Document | Chunks | Uncached in | Cache write | Cache read | Out | Cost |
|----------|--------|-------------|-------------|------------|-----|------|
| KYC | 168 | 83,963 | 62,545 | 10,445,015 | 31,263 | $0.1363 |
| Credit Facilities | 131 | 46,042 | 30,831 | 4,008,030 | 23,425 | $0.0603 |
| RBC | 122 | 39,314 | 22,561 | 2,729,881 | 22,775 | $0.0454 |
| DPDP Rules | 90 | 31,088 | 20,458 | 1,820,762 | 16,886 | $0.0323 |
| DPDP corrigendum | 1 | 787 | 876 | 0 | 130 | $0.0003 |
| **Total** | **512** | 201,194 | 137,271 | 19,003,688 | 94,479 | **$0.2746** |

One cache write per document, then reads: 98.2% of input tokens were cache reads. Contexts average 499 characters
(max 873; none empty or over 2,000). Without caching the same input would have cost about $1.93.

**A4:** a second ingest with nothing changed skipped all five documents and made no Anthropic call (run without
`ANTHROPIC_API_KEY` visible). A forced RBC re-ingest took all 122 contexts from `chunk_contexts`: $0.

**A6, retrieval** (n = 20, no LLM). Stage 1 = spec 0004's saved runs; stage 2 = chunker 2, no context; stage 3 = chunker 2
with context:

| Configuration | recall@5 s1 / s2 / s3 | recall@8 s1 / s2 / s3 | MRR@10 s1 / s2 / s3 |
|---------------|-----------------------|-----------------------|---------------------|
| dense | 0.80 / 0.80 / **0.95** | 0.85 / 0.85 / **1.00** | 0.788 / 0.788 / **0.931** |
| keyword-tsrank | 0.60 / 0.60 / 0.75 | 0.75 / 0.75 / 0.80 | 0.391 / 0.391 / 0.461 |
| keyword-bm25 | 0.80 / 0.80 / 0.85 | 0.80 / 0.80 / 0.90 | 0.722 / 0.722 / 0.768 |
| hybrid-tsrank | 0.80 / 0.80 / 0.90 | 0.85 / 0.85 / 0.95 | 0.637 / 0.637 / 0.760 |
| **hybrid-bm25** (D49) | 0.85 / 0.85 / **0.90** | 0.85 / 0.85 / **0.95** | 0.772 / 0.772 / **0.907** |

- The heading fixes changed no golden-set rank (stage 2 = stage 1 in every configuration), so the stage 3 change is
  the context's.
- The three 0004 misses, hybrid-bm25 rank s1 → s3: **rbc-01** none → **1**; **dpdp-01** 10 → **7** (now inside the 8
  passages sent); **cf-05** none → 17 (still a miss). Also cf-01 3 → 1, cf-04 2 → 1, rbc-03 2 → 1.
- Dense alone is now the best configuration on this golden set. With n = 20 the gap to hybrid-bm25 is one question at
  recall@5 and 0.024 MRR; the default (D49) is unchanged — an owner decision, not this spec's.

**R5.16, floor** (D48, hybrid-bm25 stage 3): lowest non-pinned answerable best similarity 0.657 (rbc-01, n = 19) →
**0.63** (was 0.60), applied in `AnswerOptions` and both `appsettings.json`. It stops 1 of 10 unanswerable (out-03,
0.554), the same count as before: the context lifts unanswerable similarities too (now 0.554–0.741).

**A7, answers** (hybrid-bm25, `claude-sonnet-5-5` effort medium, floor 0.63, judge `claude-haiku-4-5`, prompt v3, n = 30):

| Metric | Spec 0004 | Spec 0005 |
|--------|-----------|-----------|
| Abstained on unanswerable | 10/10 | 10/10 (9 by Claude, 1 by the floor) |
| Wrongly abstained on answerable | 2/20 | **0/20** |
| Answered and cited an expected paragraph | 17/18 | **19/20**; cf-05 answered from Paras 7 and 16 (retrieval miss) |
| Citations dropped by the verifier | 0 | 0 |
| Groundedness (judge, 1–5) | 4.89 (n = 18) | 4.90 (n = 20) |
| Relevance (judge, 1–5) | 4.33 (n = 18) | 4.45 (n = 20); lowest cf-05 = 3 |
| Cost | $0.332 + unrecorded judge | **$0.557** (answers and judge) |
| p50 latency | 4.3 s | 3.6 s |

The run's commit is `e08fff8-dirty`: the floor change and docs were not yet committed.

**A8:** `POST /ask` "Can a recovery agent call me late at night about an overdue loan?" answered that for non-microfinance
loans calls before 8:00 a.m. or after 7:00 p.m. are not allowed (Para 100, two verified quotes) and that for microfinance
loans calling before 9:00 a.m. or after 6:00 p.m. is a harsh method (Para 91). 2,817 in / 321 out, about $0.009.

**A9, spend:** $0.0006 (usage probe) + $0.2746 (contexts) + $0.557 (answer eval) + $0.009 (A8) = **$0.84** of the $2 cap.

**A5** (owner): sampled contexts are in the PR body.

## 11. Sources (checked 2026-11-24)

- Anthropic, "Introducing Contextual Retrieval" (19 Sep 2024): https://www.anthropic.com/engineering/contextual-retrieval.
  Covers the prompt, the 35% / 49% / 67% reductions in top-20 retrieval failure, "usually 50-100 tokens", context
  prepended before embedding and BM25, and "Always run evals".
- Claude cookbook, contextual embeddings guide:
  https://platform.claude.com/cookbook/capabilities-contextual-embeddings-guide. Covers the verbatim
  `DOCUMENT_CONTEXT_PROMPT` / `CHUNK_CONTEXT_PROMPT`, `cache_control` on the document block, the default 5-minute
  TTL, and the embedding input `context + "\n\n" + content`.
- Prompt caching: https://platform.claude.com/docs/en/build-with-claude/prompt-caching. Gives 512-token minimum for
  Haiku 5.5 and 4,096 for Haiku 4.5, 5-minute writes at 1.25×, reads at 0.1×, up to 4 breakpoints, and "a cache entry
  only becomes available after the first response starts".
- Pricing: https://platform.claude.com/docs/en/about-claude/pricing. Gives Haiku 4.5 at $1 / $5 per MTok and the
  cache multipliers.
- Claude Haiku 5.5: https://platform.claude.com/docs/en/models/haiku-5-5/overview,
  https://platform.claude.com/docs/en/models/haiku-5-5/whats-new-haiku-5-5 and
  https://platform.claude.com/docs/en/models/haiku-5-5/migration-guide. Cover:
  - released 7 Oct 2026; 1M context; adaptive thinking on by default; default effort medium;
  - "Omit `temperature`, `top_p`, and `top_k`"; prefill rejected; thinking tokens count toward `max_tokens`;
  - about 30% more tokens than Haiku 4.5 (our corpus measured about 1.4×);
  - $0.10 / $0.50 per MTok, cache write $0.125 and read $0.01, 5× prices over 100k tokens.
- `GET /v1/models` with the owner's key (2026-11-24) lists `claude-haiku-5-5` (created 2026-10-07) and
  `claude-haiku-4-5-20251001`.
- `POST /v1/messages/count_tokens` (2026-11-24) gives the document token counts in §5.
- `Anthropic` NuGet 13.12.0, `lib/netstandard2.0/Anthropic.xml` (local package): `AIContentCacheExtensions`.
- nomic-embed-text-v1.5 model card: https://huggingface.co/nomic-ai/nomic-embed-text-v1.5 (prefixes
  `search_document:` / `search_query:`). `ollama show nomic-embed-text` gives context length 2048 and `num_ctx` 8192.
- Corpus structure: `pdftotext` (MiKTeX) over `corpus/*.pdf` compared with the stored chunks (§1 table).
