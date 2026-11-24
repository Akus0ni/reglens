# Scaling RegLens: a one-page design

The question (sprint tracker's interview drill): *all RBI, SEBI and IRDAI circulars (tens of thousands of documents,
updated weekly), 2,000 compliance analysts, peak about 50 questions a second. Must answers cite the version in force on
a given date?*

Numbers marked **measured** come from this repo (`evals/results.md`, spec results); everything else is an
**assumption** to be checked, and is labelled as one.

## 1. Clarify first

- **Point-in-time answers:** yes, regulators amend Directions in place (RBI's "Updated as on" dates). Every chunk needs a
  document version with `effective_from` / `effective_to`, and `/ask` takes an optional `as_of` date (default today).
- **Latency target:** today p50 3.6 s per model-calling answer, measured (n = 29). Propose p95 under 8 s for `/ask`,
  under 300 ms for `/search`.
- **Tenancy and access:** one corpus for everyone; per-tenant keys, limits and cost reporting.

## 2. Sizing

| Quantity | Value | Basis |
|----------|-------|-------|
| Documents | 30,000 | assumption ("tens of thousands") |
| Chunks per document | 20 | assumption (circulars are much shorter than the 100-chunk Master Directions here: 512 chunks for 5 documents, measured) |
| Chunks | 600,000 | 30,000 × 20 |
| Vector storage | ≈ 1.8 GB as float32, ≈ 0.9 GB as `halfvec` | 600,000 × 768 dims × 4 or 2 bytes; plus HNSW graph overhead |
| Context generation, one-off | ≈ $320 | $0.2746 / 512 chunks = $0.00054 per chunk, measured; real cost depends on document length because the whole document is cached per chunk |
| Weekly updates | ≈ 300 documents ≈ $3 | assumption: 1% a week |
| Questions | 80,000 a day | assumption: 2,000 analysts × 40 a day |
| Peak | 50 QPS | given |

## 3. LLM bill

- Measured per model-calling answer: 3,896 tokens in, 444 out, **$0.0122** on `claude-sonnet-5-5` (n = 29).
- Assume 30% of questions are answered from the cache (repeated questions within a day, same index version) and 3%
  abstain at the similarity floor without a call.
- 80,000 × 0.67 × $0.0122 ≈ **$650 a day, about $14,000 a month** for 22 working days. The levers, in order:
  1. Cache hit rate: normalise questions better, and share answers across tenants.
  2. Send 5 passages instead of 8, if recall@5 holds.
  3. Prompt caching of the fixed system prompt.
  4. Route easy questions to Haiku, but only after the eval set proves it.
- **Peak:** 50 QPS × 3.6 s ≈ 180 concurrent model calls. This needs an Anthropic rate-limit tier that allows it, and a
  per-tenant concurrency cap in front.

## 4. Architecture changes

- **Ingestion becomes an async pipeline:** crawler (per regulator, respecting `robots.txt` and terms; RBI blocks
  scripted downloads today) → object storage for PDFs → queue → parse/chunk workers → context workers (Haiku, prompt
  cache per document) → embedding workers → index writer. It stays idempotent by content hash and pipeline version,
  as now (spec 0005). A new version of a document closes the old version's `effective_to` instead of deleting it.
- **Storage:** Postgres keeps metadata, versions and BM25 statistics. pgvector's HNSW gives the best speed/recall
  trade-off but "has slower build times and uses more memory" (pgvector README). At about 1 GB of vectors it fits one
  well-sized instance. Move to a dedicated vector store only if index builds or memory become the bottleneck at the
  measured chunk count. The BM25 materialized views need incremental refresh, or a search engine, beyond a few
  million chunks.
- **Read path:** HybridCache (memory, then Redis) → hybrid retrieval filtered by `as_of` → optional reranker (only if
  the larger golden set shows it helps) → Sonnet → the code verifier. The API stays stateless, behind a load balancer.
- **Embeddings:** local Ollama doesn't scale to 50 QPS of query embeddings on one GPU. Use a hosted embedding API or a
  GPU pool, and re-run the retrieval evals when the model changes.

## 5. Agent orchestration (reglens-agent)

- **A fixed workflow where possible:** plan and assess are single calls. Only research is a loop, bounded (4 turns
  per question, 20 tool calls per review, repeated calls refused).
- **Budgets per review:** measured $0.045 and 42 s p50 (n = 30). A per-tenant monthly budget stops new reviews, not
  running ones.
- **Durable waits:** reviews can wait days for approval. The Postgres checkpointer already survives restarts (tested).
  Add expiry, and a reminder, for stale reviews.
- **Scoped access:** the agent's MCP key is read-only and rate-limited, as now; filing uses a separate credential
  with write access to one repository.

## 6. Observability and guardrails

- One trace per request across agent → MCP → RegLens → Anthropic (works today). Add cost per tenant from the GenAI
  span attributes.
- Evals in two places. The deterministic gate runs on every prompt change: record and replay, CI blocks stale or
  worse results (spec 0002). A nightly sample of live questions goes to the judge, with a monthly human hand check
  of the judge.
- Prompt-injection tests with poisoned passages (RegLens has one probe today; grow it into a suite).
- **PII:** RegLens traces include prompts by default (D62). At scale: turn content capture off, or redact, and keep
  user questions out of logs. Questions can contain customer data.
