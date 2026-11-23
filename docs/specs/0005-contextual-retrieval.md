# Spec 0005 — Contextual retrieval and heading fixes

> Status: **Draft** · Created: 2026-11-24 · Owner: @Akus0ni
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

## 9. Sources (checked 2026-11-24)

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
