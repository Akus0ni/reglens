# Spec 0001 — Ingest the corpus

> Status: **Draft — awaiting owner approval** · Created: 2026-11-23 · Owner: @Akus0ni
> Depends on: none (first code) · Blocked by open questions: none (see §8 for choices to confirm)
> Sprint phase: D1·2 (plus the solution scaffold and local infrastructure)

## 1. Context

RegLens answers questions from four official documents in `corpus/` (D6, D7). Before anything can be searched, each
document has to become **chunks**: clean passages that keep their regulatory address (document, chapter, paragraph,
page), stored in Postgres with an embedding for vector search and a `tsvector` for keyword search (D10).

This spec also lays the foundations every later spec builds on: the clean-architecture solution, the build and test
conventions, the database schema and its migrations, and a one-command local environment.

What the corpus looks like (measured on 2026-11-23 with a quick text extraction):

| Document | Structure | Size (≈ tokens, chars ÷ 4) |
|----------|-----------|------------------------------|
| RBI NBFC KYC Directions | Chapters I–XI → lettered sections (A., B.) → numbered paragraphs `N.` → sub-clauses `(1)`, `(i)`, `(a)`; annexes | ≈ 47k |
| RBI NBFC Credit Facilities Directions | Same shape; Chapter III is Digital Lending; paragraphs run 1–115 | ≈ 23k |
| DPDP Rules (pages 24–41) | `N. Title. — (1) …` rules, then Schedules | ≈ 16k |
| DPDP corrigendum | One page of numbered corrections | < 1k |

Noise seen in extraction: table-of-contents lines with dotted leaders, standalone page numbers, and Hindi masthead
lines that extract as broken characters.

## 2. Decisions

Builds on: D4 (Ollama `nomic-embed-text`), D6–D8 (corpus), D9 (stack), D10 (Postgres schema), D12 (contextual
retrieval, later), D15 (SELECT-only API role).

New, decided by the owner on 2026-11-23:

| # | Decision |
|---|----------|
| D22 | **Clean architecture, 4 layers + 2 hosts.** `RegLens.Domain` (no references) ← `RegLens.Application` ← `RegLens.Infrastructure`; hosts `RegLens.Ingest` (console) and `RegLens.Api` (web) are composition roots only. SOLID throughout; no interface without a second implementation or a test seam |
| D23 | **Ingestion runs from both** the console host and an API trigger (resolves Q2). Both call the same use case |
| D24 | **Schema via versioned SQL scripts** (`NNN_name.sql`) applied in order by a small runner and recorded in a `schema_version` table |
| D25 | **Build and test conventions mirror InvoiceGen**: `global.json` SDK pin with the Microsoft.Testing.Platform runner, Central Package Management, shared `Directory.Build.props` (nullable, warnings as errors, `latest-recommended` analysis), xUnit v3, AwesomeAssertions, NSubstitute, Testcontainers |
| D26 | **Local infrastructure via `docker-compose.yml`**: Postgres with pgvector and the standalone Aspire dashboard. Ollama keeps running natively (installed on D:) |

Proposed by this spec — **confirm by approving the spec PR**, or say which to change:

| # | Proposal | Why |
|---|----------|-----|
| P1 | The **API trigger is specified here but built with spec 0005**, when `RegLens.Api` gets its API key (Q3). Spec 0001 builds the use case and the console host | An unauthenticated write endpoint would be the first thing the API exposes. The use case is host-agnostic, so adding the trigger later is a thin adapter |
| P2 | **Chunk = one numbered paragraph** (RBI) or **rule** (DPDP), with its heading trail. Paragraphs over **500 estimated tokens** split at sub-clause boundaries; tiny neighbours are never merged across paragraph numbers | Citations must point at one paragraph. 500 is the plan's upper bound and far below the model's 2K context |
| P3 | **Token estimate = characters ÷ 4**, not a tokenizer | Only used to keep chunks small; a tokenizer dependency is not worth it here. The real limit (2K) leaves a 4× margin |
| P4 | **Content hash = SHA-256 of `documentId + "\n" + paraRef + "\n" + content`** | The two RBI documents share boilerplate (for example the repeal-and-saving paragraphs). Hashing content alone would drop one of them |
| P5 | **A document whose SHA-256 changed is replaced**: its old chunks are deleted and the new ones inserted in one transaction. Unchanged documents are skipped | The plan keeps only the current version of each document; RBI updates directions in place |
| P6 | **The corrigendum is ingested as its own document**, not patched into the Rules text | Answers can then cite it directly, and the Rules text stays exactly as published |
| P7 | **Npgsql 10.0.4, not 11.0.0** | `Pgvector` 0.3.2 is built against Npgsql 8.x; compatibility with Npgsql 11 is not verified. Revisit when Pgvector says so |
| P8 | **The `context` column exists but stays empty**; contextual retrieval (Haiku) fills it in spec 0004, as the plan's D1·5 says | Keeps this spec free of Anthropic calls, so ingestion is runnable and testable with no API key |

## 3. Scope

**In scope:** solution scaffold (D22, D25); `docker-compose.yml` (D26); schema and migration runner (D24); reading
and verifying `corpus/manifest.json`; PDF text extraction and cleaning; structure-aware chunking; embedding through
Ollama with backpressure and retries; idempotent storage; the console host; tests at every layer; README-level run
instructions in `CLAUDE.md`.

**Out of scope:** contextual retrieval (spec 0004, P8); search and `/search` (spec 0002); `RegLens.Api` and the
ingest trigger (spec 0005, P1); database roles and OpenTelemetry export (spec 0005); tables inside RBI annexes beyond
plain text (known limitation); any Anthropic API call.

## 4. Requirements

### 4.1 Solution and conventions

| ID | Requirement |
|----|-------------|
| R1.1 | `global.json` pins SDK `10.0.404` with `rollForward: latestPatch` and `test.runner: Microsoft.Testing.Platform` |
| R1.2 | `Directory.Build.props`: `net10.0`, nullable, implicit usings, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`. `Directory.Packages.props` holds every package version; no `.csproj` has `Version=` |
| R1.3 | Projects: `src/RegLens.Domain`, `src/RegLens.Application`, `src/RegLens.Infrastructure`, `src/RegLens.Ingest`; tests `tests/RegLens.Domain.Tests`, `tests/RegLens.Application.Tests`, `tests/RegLens.Infrastructure.Tests`, `tests/RegLens.Architecture.Tests`. All in `RegLens.slnx` |
| R1.4 | Architecture tests fail the build if `Domain` references any RegLens project or any package other than the BCL, if `Application` references `Infrastructure` or a host, or if `Infrastructure` references a host |

### 4.2 Local infrastructure

| ID | Requirement |
|----|-------------|
| R1.5 | `docker-compose.yml` runs `pgvector/pgvector:pg17` (port 5432, named volume, password from an env var with a dev default documented in `CLAUDE.md`) and `mcr.microsoft.com/aspire/dashboard:latest` (UI 18888, OTLP gRPC 4317 → 18889, OTLP HTTP 4318 → 18890) |
| R1.6 | Connection string, Ollama endpoint, embedding model, corpus path and channel settings come from configuration (`appsettings.json` + user-secrets + env vars). No secret is committed |

### 4.3 Schema

| ID | Requirement |
|----|-------------|
| R1.7 | Migrations are `NNN_name.sql` files embedded in `RegLens.Infrastructure`, applied in order inside a transaction each, and recorded in `schema_version (version, name, applied_at)`. Re-running applies nothing new |
| R1.8 | `001_init.sql` creates: extension `vector`; `documents (id text PK, title, reference, version, source_url, sha256, ingested_at)`; `chunks (id bigserial PK, document_id FK, para_ref, heading, page_start, page_end, content, context NULL, content_hash UNIQUE, token_estimate, embedding vector(768) NOT NULL, tsv tsvector GENERATED from context + content, created_at)`; an HNSW index on `embedding vector_cosine_ops`; a GIN index on `tsv`; a B-tree index on `document_id` |
| R1.9 | A migration that fails leaves the database unchanged and stops ingestion with the file name in the error |

### 4.4 Reading the corpus

| ID | Requirement |
|----|-------------|
| R1.10 | The manifest is read into domain objects. A missing file, a size or SHA-256 mismatch, or an unknown field shape stops ingestion **before any write**, naming the document |
| R1.11 | Text comes from PdfPig's `ContentOrderTextExtractor`, never `page.Text`. Only `ingest_pages` are read when the manifest sets them (DPDP Rules: 24–41) |
| R1.12 | Cleaning removes table-of-contents lines (dotted leaders followed by a page number), standalone page numbers, lines that are mostly non-Latin script, and repeated headers; it keeps paragraph and clause numbers intact. Page numbers are kept as metadata |

### 4.5 Chunking (Domain, pure)

| ID | Requirement |
|----|-------------|
| R1.13 | RBI documents: a chunk starts at each numbered paragraph `N.` and carries a heading trail `Chapter III – Digital Lending › A. General Requirements …`. `para_ref` is `Para N` (and `Para N(k)` for split parts). Annexes get `Annex I`, `Annex II` … with their own numbering |
| R1.14 | DPDP Rules: a chunk starts at each rule `N. Title. —`; `para_ref` is `Rule N`. Schedules get `First Schedule` … with their item numbers. The corrigendum is one chunk, `para_ref` `Corrigendum` |
| R1.15 | A chunk over 500 estimated tokens (P2, P3) splits at the last sub-clause boundary (`(1)`, `(i)`, `(a)`) before the limit; each part repeats the paragraph's opening line so it stands alone. A chunk never spans two paragraph numbers |
| R1.16 | Every chunk records `page_start` and `page_end` from the source PDF |
| R1.17 | The chunker is deterministic: the same text always produces the same chunks and hashes (P4) |

### 4.6 Embedding and storage

| ID | Requirement |
|----|-------------|
| R1.18 | Embeddings come from OllamaSharp's `OllamaApiClient` used as `Microsoft.Extensions.AI` `IEmbeddingGenerator<string, Embedding<float>>`, model `nomic-embed-text`. The input is `"search_document: " + content` (the model card's task prefix). A vector that is not 768 long is an error |
| R1.19 | Chunks flow through a **bounded** `Channel<T>` (default capacity 32) to 2 embedding consumers (configurable 1–4), so a slow Ollama slows the reader instead of filling memory |
| R1.20 | The Ollama `HttpClient` uses `AddStandardResilienceHandler()` (retry with backoff, timeout, circuit breaker) |
| R1.21 | Per document, in one transaction: unchanged SHA-256 → skip; new or changed → upsert the `documents` row, delete its old chunks, insert the new ones (P5). Inserts are `ON CONFLICT (content_hash) DO NOTHING` |
| R1.22 | Vectors are written through the `Pgvector` package (`NpgsqlDataSourceBuilder.UseVector()`) |

### 4.7 Console host

| ID | Requirement |
|----|-------------|
| R1.23 | `dotnet run --project src/RegLens.Ingest` applies migrations, then ingests every manifest document. Options: `--force` (re-ingest unchanged documents), `--document <id>` (one document) |
| R1.24 | It logs per document: status (skipped / ingested / replaced), pages read, chunks written, smallest / median / largest token estimate, and elapsed time; then a total. Exit code 0 on success, non-zero on any failure |
| R1.25 | Ctrl+C stops cleanly: the current document's transaction rolls back and nothing half-written remains |

### 4.8 API trigger (specified now, built in spec 0005 — P1)

| ID | Requirement |
|----|-------------|
| R1.26 | `RegLens.Api` will expose an authenticated ingest trigger that calls the same use case, uses a separate write-capable connection string, and refuses to run while another ingestion is in progress |

## 5. Design

```text
corpus/manifest.json ─► ManifestCatalog (Infra) ─► IngestCorpus (Application use case)
                                                     │  for each document:
                                                     │    verify sha256 ─► skip if unchanged
                                                     │    IDocumentTextSource.ReadPages  (PdfPig, Infra)
                                                     │    TextCleaner + Chunker          (Domain, pure)
                                                     │    bounded Channel ─► N consumers ─► IEmbeddingGenerator (Ollama)
                                                     └─►  IChunkStore.ReplaceDocument    (Npgsql + pgvector, Infra, 1 tx)
```

- **Domain**: `SourceDocument`, `Chunk`, `ParagraphRef`, `ContentHash`, `TokenEstimate`, `TextCleaner`, `Chunker`
  (strategy per document kind: RBI directions, DPDP rules, corrigendum). No I/O.
- **Application**: `IngestCorpus` use case and the ports it owns: `ICorpusCatalog`, `IDocumentTextSource`,
  `IChunkStore`. It depends on `IEmbeddingGenerator<string, Embedding<float>>` from
  `Microsoft.Extensions.AI.Abstractions` directly (a stable abstraction; wrapping it would add nothing).
- **Infrastructure**: `ManifestCatalog`, `PdfPigTextSource`, `PostgresChunkStore`, `SqlMigrationRunner`, DI extension
  `AddRegLensInfrastructure(configuration)`.
- **RegLens.Ingest**: `Host.CreateApplicationBuilder`, configuration, DI, command-line options, exit code.

Model calls: **Ollama only**, one embedding per new chunk (≈ 300–500 chunks for the whole corpus, estimated from the
paragraph counts). **No Anthropic calls** in this spec (P8).

Packages (versions checked on NuGet on 2026-11-23): PdfPig 0.1.16, OllamaSharp 5.5.2, Microsoft.Extensions.AI.Abstractions
11.0.1, Npgsql 10.0.4 (P7), Pgvector 0.3.2, Microsoft.Extensions.Hosting 10.0.15, Microsoft.Extensions.Http.Resilience
10.11.1; tests: xunit.v3 4.0.3, AwesomeAssertions 9.6.0, NSubstitute 6.2.0, Testcontainers.PostgreSql 4.17.0.

### System design concepts exercised

| Concept | Where | What could go wrong without it |
|---------|-------|--------------------------------|
| Idempotent writes | R1.21, P4 | Running ingest twice doubles the index and skews every search score |
| Backpressure (bounded queue) | R1.19 | Reading outpaces Ollama; memory grows and Ollama times out under a flood |
| Retry with backoff, circuit breaker | R1.20 | One Ollama hiccup fails the whole run, or retries hammer it while it restarts |
| Data provenance and integrity | R1.10, R1.16 | An answer cites a file that silently differs from the official version |
| Versioned schema migrations | R1.7–R1.9 | Schema drift between machines; no safe way to change the table later |
| Atomic replace | R1.21, R1.25 | A crash mid-document leaves old and new chunks mixed |
| Dependency inversion / clean boundaries | D22, R1.4 | Domain logic tied to PdfPig or Npgsql, untestable without a database |

## 6. Acceptance criteria

| # | Check | How |
|---|-------|-----|
| A1 | Solution builds with zero warnings | `dotnet build RegLens.slnx` |
| A2 | All tests pass, including architecture tests | `dotnet test` (Docker running for Testcontainers) |
| A3 | Chunker unit tests cover: paragraph split, heading trail, sub-clause split over 500, no cross-paragraph chunk, TOC and page-number removal, DPDP rule and schedule refs, determinism | `dotnet test --project tests/RegLens.Domain.Tests` |
| A4 | Migration tests on a real pgvector container: applies once, re-run applies nothing, vector round-trips, `tsv` is generated | `RegLens.Infrastructure.Tests` |
| A5 | Use-case tests with substitutes: unchanged document skipped; changed document replaced; bad SHA-256 stops before any write; embedding input has the `search_document: ` prefix | `RegLens.Application.Tests` |
| A6 | Full run on the real corpus logs per-document counts and a total, exit code 0 | `docker compose up -d`, then `dotnet run --project src/RegLens.Ingest` |
| A7 | Running it again writes nothing new; `SELECT count(*) FROM chunks` is unchanged | second run + SQL |
| A8 | 10 random chunks read cleanly, with the right `para_ref` and pages (owner spot-check against the PDFs) | SQL `ORDER BY random() LIMIT 10` |
| A9 | Every stored vector has 768 dimensions | `SELECT DISTINCT vector_dims(embedding) FROM chunks` |

## 7. Measurements

Record in `docs/project-log.md` (with date, Ollama model tag and corpus manifest date): chunks per document, token
estimate min / median / max, total ingest time, and the A8 spot-check result (k of 10 correct).

## 8. Open questions

| # | Question | Blocks |
|---|----------|--------|
| — | Confirm or change P1–P8 by approving or commenting on this PR | Code PR for 0001 |

## 9. Sources (checked 2026-11-23)

- PdfPig README: use `ContentOrderTextExtractor` for RAG, not `page.Text` — https://github.com/UglyToad/PdfPig
- pgvector .NET README (Npgsql): `UseVector()`, `Vector`, `CREATE EXTENSION`, `ReloadTypes()` — https://github.com/pgvector/pgvector-dotnet
- Nomic model card: task prefixes `search_document:` / `search_query:` — https://huggingface.co/nomic-ai/nomic-embed-text-v1.5
- Ollama library: `nomic-embed-text`, 274 MB, 2K context window — https://ollama.com/library/nomic-embed-text
- OllamaSharp source: `OllamaApiClient` implements `IEmbeddingGenerator<string, Embedding<float>>`; has an
  `HttpClient` constructor — https://github.com/awaescher/OllamaSharp/blob/main/src/OllamaSharp/OllamaApiClient.cs
- Microsoft Learn, IEmbeddingGenerator — https://learn.microsoft.com/dotnet/ai/iembeddinggenerator
- Standalone Aspire dashboard: image `mcr.microsoft.com/aspire/dashboard:latest`, ports 18888 / 18889 / 18890 —
  https://aspire.dev/dashboard/standalone/ (the image name in the sprint tracker and `CLAUDE.md` is the older
  `mcr.microsoft.com/dotnet/aspire-dashboard`; the code PR updates `CLAUDE.md`)
- NuGet package pages for each version listed in §5; Npgsql 11.0.0 targets net10.0, Pgvector 0.3.2 depends on
  Npgsql ≥ 8.0.5 (nuspec)
- Microsoft Learn, System.Threading.Channels — https://learn.microsoft.com/dotnet/core/extensions/channels
- Microsoft Learn, HTTP resilience — https://learn.microsoft.com/dotnet/core/resilience/http-resilience
- Not re-checked in this session (carried from the plan): pgvector HNSW / `vector_cosine_ops` and Postgres generated
  `tsvector` columns. The code PR verifies them against the pgvector README and Postgres docs before writing
  `001_init.sql`.
