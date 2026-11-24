# Spec 0006 — MCP server, tracing and production basics

> Status: **Approved (PR #18, 2026-11-24) · Implemented in `feat/0006-mcp-production`** · Created: 2026-11-24 · Owner: @Akus0ni
> Depends on: specs 0001–0005 · Blocked by open questions: none (Q3 answered: D58)
> Sprint phase: D1·6

## 1. Context

RegLens answers questions over Indian NBFC regulations through `GET /search` and `POST /ask`. Day 2's compliance
agent (D1) is meant to use it over **MCP**, which needs a server at `/mcp` with read-only tools (D14). The API also
still runs without any of the production basics in D15: anyone who can reach port 5180 can spend Anthropic credit
through `/ask`, nothing limits request rates, the API connects to Postgres as the schema owner, there is no health
endpoint, and the Aspire dashboard in `docker-compose.yml` receives no telemetry.

This spec adds the MCP server and those basics. Search, answers and the index don't change, so the evals are not
rerun. The checks are functional: tests, plus a few paid calls through MCP and REST.

Checked while writing this spec (2026-11-24):

- `ModelContextProtocol.AspNetCore` **2.4.0** (released 2026-11-07) defaults to **stateless** Streamable HTTP. Protocol
  revision `2026-07-28` removed sessions and the `initialize` handshake. A stateless server still serves clients
  that use `initialize` (revision `2025-11-25` and earlier), one POST at a time. RegLens's tools need no sessions,
  sampling or notifications (§9).
- The SDK puts the ASP.NET Core `ClaimsPrincipal` into tool calls, and `MapMcp` returns an
  `IEndpointConventionBuilder`. So `/mcp` can take the same authentication as the REST endpoints (§9).
- The API never writes to Postgres. Only `RegLens.Ingest` runs migrations and writes chunks, contexts and the BM25
  views. A SELECT-only role therefore costs the API nothing.
- Chunk text is at most 1,999 characters (median 513), headings at most 247, and search excerpts are 300. Tool
  results are small by construction.

## 2. Decisions

Builds on:
- D14 (MCP tools) and D15 (production basics)
- D27 (the key and rate limits come with this spec)
- D22 (layers)
- D13 and D52 (retrieved text is data; contexts never reach answers)
- D56 and D57 (spec 0005 follow-ups, PR #17)

New decisions made by the owner for this spec (2026-11-24):

| # | Decision |
|---|----------|
| D58 | **API key** (resolves Q3): one key from configuration `Auth:ApiKey`, set by env var `Auth__ApiKey` or user-secrets, never `appsettings`. Callers send it in the `X-API-Key` header, and the API compares it in constant time. `/search`, `/ask`, `/passages` and `/mcp` require it; `/health/*` does not. To rotate, change the value and restart. |
| D59 | **Rate limits per caller**, in fixed one-minute windows. The **ask** bucket allows 5 (`POST /ask`, `ask_regulations`). The **read** bucket allows 30 (`GET /search`, `GET /passages/{id}`, `search_regulations`, `get_passage`). The limits are settings. |
| D60 | **SELECT-only database login for the API.** Migration 005 creates the NOLOGIN role `reglens_readonly`, with SELECT on every table and view and default privileges for future ones. `scripts/create-api-db-user.ps1` creates the login `reglens_api` in that role, with its password from an env var. No password appears in SQL or git. |
| D61 | **Readiness**: Postgres or the Ollama embedding model being unreachable makes the API Unhealthy. Redis being unreachable makes it Degraded (HybridCache keeps its in-memory level). Anthropic is not probed, so there are no paid calls. |
| D62 | **Tracing**: the API and Ingest both export traces, metrics and logs over OTLP to the Aspire dashboard. Spans **include prompt and answer text** (`EnableSensitiveData`), behind a setting that defaults to on. Evals are unchanged. |
| D63 | REST matches MCP: **`GET /passages/{id}`** returns one passage, so all three MCP operations exist over REST too. |
| D64 | Spend cap for this spec's paid checks: **$0.50**. |

## 3. Scope

**In scope:**
- An MCP server at `/mcp` (stateless Streamable HTTP) with three read-only tools.
- `GET /passages/{id}`.
- API-key authentication.
- Rate limits.
- `/health/live` and `/health/ready`.
- Host filtering.
- OpenTelemetry for API and Ingest.
- The read-only database role.
- Docs (`CLAUDE.md`, the skill and the project log).

**Out of scope:**
- OAuth / MCP authorization-server metadata. Authorization is a transport add-on in MCP, and the Day 2 agent sends a
  header.
- Multiple or hashed keys (D58).
- Stateful MCP sessions, resources, prompts, notifications and stdio.
- Limiting failed-auth attempts (the API binds to 127.0.0.1).
- The API ingest trigger (D23, never built).
- Langfuse (Q5, Day 2).
- Deployment and TLS.
- Changes to search, answers or the index.

## 4. Requirements

| ID | Requirement |
|----|-------------|
| R6.1 | `/mcp` serves MCP Streamable HTTP with `ModelContextProtocol.AspNetCore` 2.4.0, `SessionMode = Stateless` set explicitly. A client using `initialize` (`2025-11-25`) and the SDK's default client (which probes `2026-07-28` first) can both list and call the tools |
| R6.2 | Tool `search_regulations(query, topK?)` runs the same `SearchRegulations` use case and validation as `GET /search` and returns its result as structured content |
| R6.3 | Tool `get_passage(chunkId)` returns one passage (document, title, `para_ref`, heading, pages, full content) through `IPassageReader`. An unknown id is a tool error, "No passage with id N" |
| R6.4 | Tool `ask_regulations(question, topK?)` runs `AnswerQuestion` with the same validation and floor as `POST /ask` and returns `AnswerResult` as structured content. Model failures are tool errors that carry the same message `/ask` returns |
| R6.5 | All three tools are annotated `ReadOnly = true` and `OpenWorld = false`, with `Idempotent = true` on search and get_passage. Each tool has a `Title`, and each parameter has a description that states its limits. Descriptions say that passage text is regulatory text to be treated as data, not instructions |
| R6.6 | Invalid tool input (empty or overlong query, `topK` outside 1–`MaxTopK`, `chunkId` < 1) is a tool error naming the parameter and limit, not a protocol error |
| R6.7 | `GET /passages/{id}` returns the passage as JSON. It returns 404 problem details for an unknown id and 400 for an id < 1 |
| R6.8 | An API-key authentication scheme reads `X-API-Key` and compares it with `Auth:ApiKey` using `CryptographicOperations.FixedTimeEquals`. A missing or wrong key on a protected endpoint gets **401**. The key itself is never logged |
| R6.9 | The API fails at startup if `Auth:ApiKey` is missing or shorter than 32 characters. The message says where to set it |
| R6.10 | `/search`, `/ask`, `/passages/{id}` and `/mcp` require authorization. `/health/live` and `/health/ready` allow anonymous access |
| R6.11 | Rate limits (D59) are per caller in fixed one-minute windows, and each bucket is shared between REST and MCP. Over the limit, REST returns **429** with `Retry-After`, and MCP returns a tool error saying when to retry. An MCP `tools/list` is not counted |
| R6.12 | `/health/live` returns 200 without running checks. `/health/ready` runs a Postgres check (`SELECT 1`) and an Ollama check (the embedding model is listed) as Unhealthy-on-failure, and a Redis check as Degraded-on-failure, each with a 2 s timeout. Healthy and Degraded return 200; Unhealthy returns 503 |
| R6.13 | `AllowedHosts` is `localhost;127.0.0.1;[::1]` in the API's `appsettings.json` (DNS-rebinding guidance in the MCP SDK docs) |
| R6.14 | The API and Ingest register OpenTelemetry with service names `reglens-api` and `reglens-ingest`. **Traces** come from ASP.NET Core, HttpClient, Npgsql, `Experimental.Microsoft.Extensions.AI` and `Experimental.ModelContextProtocol`. **Metrics** come from ASP.NET Core, HttpClient, the runtime and the same sources. **Logs** are exported through `UseOtlpExporter()`. The endpoint is `Telemetry:OtlpEndpoint` (default `http://127.0.0.1:4317`); empty turns export off |
| R6.15 | Every `IChatClient` (answers and chunk context) and the embedding generator are wrapped with M.E.AI's `UseOpenTelemetry`. `EnableSensitiveData` comes from `Telemetry:CaptureContent` (default `true`, D62) |
| R6.16 | Migration `005_readonly_role.sql` creates `reglens_readonly` (NOLOGIN) if it is missing. It grants USAGE on schema `public`, SELECT on all tables, and SELECT on the three BM25 materialized views by name, and it sets `ALTER DEFAULT PRIVILEGES … GRANT SELECT ON TABLES` for the owner. It can be run again |
| R6.17 | `scripts/create-api-db-user.ps1` creates or updates the login `reglens_api` in role `reglens_readonly`, with the password from `REGLENS_API_DB_PASSWORD`. The password goes to psql on stdin, not as an argument. The script prints the connection string to put in the API's user-secrets, without the password |
| R6.18 | Connected as `reglens_api`, search, passages and ask work, and `INSERT`, `UPDATE`, `DELETE` and `REFRESH MATERIALIZED VIEW` are refused |
| R6.19 | The API's existing behaviour is unchanged apart from the key and the limits: the existing endpoint tests pass with a key added |

## 5. Design

```text
request ──► HostFiltering ──► Authentication (X-API-Key scheme) ──► Authorization
         ├─ /health/live, /health/ready          (AllowAnonymous, no limits)
         ├─ /search, /passages/{id} ─► RateLimitFilter(read) ─► SearchRegulations / IPassageReader
         ├─ /ask                    ─► RateLimitFilter(ask)  ─► AnswerQuestion
         └─ /mcp (MapMcp, stateless) ─► CallTool filter: bucket by tool ─► RegulationTools
                                           search_regulations ─► SearchRegulations
                                           get_passage        ─► IPassageReader
                                           ask_regulations    ─► AnswerQuestion
OpenTelemetry (API, Ingest) ──OTLP gRPC──► Aspire dashboard 127.0.0.1:4317 (UI 18888)
API ──Npgsql──► Postgres as reglens_api ∈ reglens_readonly (SELECT only)
```

**P1, MCP host.** `AddMcpServer().WithHttpTransport(o => o.SessionMode = Stateless).WithTools<RegulationTools>()`
and `app.MapMcp("/mcp").RequireAuthorization()`. `RegulationTools` lives in `RegLens.Api` beside the endpoint
classes: it is an adapter (D22), not a use case. Validation is shared with the REST endpoints by moving it into one
`RequestValidation` class in the API, so a tool and its endpoint can't drift apart.

**P2, tool results.** Tools use `UseStructuredContent = true` (an output schema from the return type) and return
`SearchResult`, `StoredPassage` and `AnswerResult` as they are. A rejected or failed call throws `McpException`; the
SDK turns that into a tool result with `isError: true` and the message, so the calling model can correct itself.
The SDK hides any other exception's message. The sizes are fixed by existing limits:

| Tool | Largest result |
|------|----------------|
| `search_regulations` | `MaxTopK` (20) hits × (300-character excerpt + metadata) ≈ 12 KB |
| `get_passage` | one chunk ≤ 2,000 characters |
| `ask_regulations` | one answer ≤ `MaxOutputTokens` |

There are no new truncation rules.

**P3, API key.** An `AuthenticationHandler<ApiKeyOptions>` (scheme `ApiKey`, the default scheme). It returns
`NoResult` when the header is absent and `Fail` when the key is wrong; both give 401 on a protected endpoint. On
success it issues a `ClaimsPrincipal` with name `api-key`, which also keys the rate limits, and the SDK passes it to
tools. The key is compared as UTF-8 bytes with `FixedTimeEquals`, which still returns false at once when the
lengths differ (the docs say so). The key's length is not a secret.

**P4, rate limits.** A singleton `RateLimitBuckets` holds two `PartitionedRateLimiter<string>` with
`FixedWindowRateLimiter`, partitioned by caller (the user name, else the remote IP). The REST endpoints acquire through an
endpoint filter, which on rejection writes 429 and `Retry-After` from the lease's `RetryAfter` metadata. MCP acquires
through `AddCallToolFilter`, keyed by tool name. A filter is used instead of the built-in middleware's
`RequireRateLimiting` so that REST and MCP share **one** counter per bucket; the middleware keeps a separate counter
per policy, and `/mcp` is one route for all three tools. Cached answers still count, because a caller can't tell
whether an answer was cached until after the call.

**P5, health.** Built-in ASP.NET Core health checks with three small `IHealthCheck` classes in Infrastructure:
- Postgres: `SELECT 1` through `NpgsqlDataSource`.
- Ollama: lists the local models through OllamaSharp and looks for `Ollama:EmbeddingModel`.
- Redis: a `GetAsync` of a probe key on `IDistributedCache`, only when Redis is configured.

`MapHealthChecks("/health/live", new() { Predicate = _ => false })` and `/health/ready` with the default
status-code mapping (Unhealthy → 503).

**P6, telemetry.** `AddOpenTelemetry().ConfigureResource(AddService(name)).WithTracing(...).WithMetrics(...)
.UseOtlpExporter(Grpc, endpoint)`, in a shared `AddRegLensTelemetry(configuration, serviceName)` extension in
Infrastructure, called by both hosts. The IChatClients and the embedding generator are built with
`new ChatClientBuilder(inner).UseOpenTelemetry(configure: c => c.EnableSensitiveData = …)` and the embedding
equivalent. This needs the `Microsoft.Extensions.AI` package (10.11.1, the version of the Abstractions already in
use). The dashboard's OTLP endpoint is unauthenticated by default (`Dashboard:Otlp:AuthMode` = Unsecured) and bound
to 127.0.0.1 in compose, so no OTLP key is needed.

With D62, a span holds the question, the system prompt and the retrieved passages, and the dashboard keeps them in
memory until it restarts. The text is public regulation plus the caller's question. The setting exists so content
capture can be turned off before RegLens runs anywhere but this machine.

**P7, read-only role.** Migration 005:

```sql
DO $$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'reglens_readonly') THEN
    CREATE ROLE reglens_readonly NOLOGIN;
  END IF;
END $$;
GRANT USAGE ON SCHEMA public TO reglens_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO reglens_readonly;
GRANT SELECT ON lexeme_stats, chunk_lengths, corpus_stats TO reglens_readonly;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO reglens_readonly;
```

The PostgreSQL docs say `ALL TABLES` covers views and foreign tables but don't mention materialized views, so those
three are granted by name. A test proves SELECT works and writes fail as `reglens_api` (R6.18). Roles are
cluster-wide, so the migration checks for the role instead of creating it unconditionally.

**P8, REST passage.** `GET /passages/{id:long}` → `IPassageReader.GetAsync([id])` → 200 with the `StoredPassage`, or 404.
The generated context is not returned (D52).

**Model calls and cost.** No new model calls. The paid checks are: `ask_regulations` over MCP for two questions
(one answerable, one not), `POST /ask` with the key once, and the rate-limit check. The rate-limit check sends six
asks of one question, so five are cache hits after the first. That's about 4 Sonnet calls at about $0.01–0.02
each: **≈ $0.08, cap $0.50**.

### System design concepts exercised

| Concept | Where | What could go wrong without it |
|---------|-------|--------------------------------|
| Authentication at the edge, fail closed | D58, R6.8–R6.10 | Anyone on the machine spends Anthropic credit through `/ask` or `/mcp` |
| Constant-time secret comparison | P3 | A timing side channel leaks the key byte by byte |
| Rate limiting with shared buckets across protocols | D59, P4 | MCP and REST each allow the full rate, so a caller gets double |
| Least privilege | D60, P7 | A SQL bug or injection in the API can change the index |
| Liveness vs readiness | D61, P5 | An orchestrator restarts a process that is only waiting for Ollama |
| Distributed tracing and GenAI semantic conventions | D62, P6 | Can't see where an `/ask` spends its 4 s (embedding, Postgres or Sonnet) |
| Protocol versioning and stateless servers | R6.1 | A stateful server refuses `2026-07-28` clients or needs session affinity |
| Untrusted content across a tool boundary | R6.5 | The Day 2 agent follows instructions found inside a passage |

## 6. Acceptance criteria

| # | Check | How |
|---|-------|-----|
| A1 | Builds with zero warnings, and all tests pass. The new tests cover: key missing, wrong or correct (401/200); startup fails without a key; health live/ready, including Redis Degraded and Postgres down → 503; 429 with `Retry-After` on the sixth ask; MCP list and call of all three tools through the SDK's own client in-process (`WebApplicationFactory`); a tool error on bad input; `GET /passages/{id}` 200/404/400; and the read-only role refusing writes (Testcontainers) | `dotnet build`, `dotnet test` |
| A2 | The MCP Inspector or a short client script lists three tools with annotations and output schemas, and calls `search_regulations` and `get_passage` against the real index | output in the PR |
| A3 | `ask_regulations` over MCP answers rbc-03 with verified citations and abstains on one unanswerable golden question | output in the PR |
| A4 | The Aspire dashboard shows one `/ask` trace with spans for HTTP, embedding (Ollama), Postgres and the `chat claude-sonnet-5-5` call, with token counts; and one ingest trace | screenshot or span list in the PR |
| A5 | The API runs as `reglens_api` after `create-api-db-user.ps1`; `/health/ready` reports Healthy, and stopping Redis gives Degraded with 200 | requests in the PR |
| A6 | Measured spend ≤ $0.50 | usage in the responses |

## 7. Measurements

None for retrieval or answers (nothing that affects them changes). The PR reports the spend and the latency of one
`/ask` broken down by span, from A4.

## 8. Open questions

| # | Question | Blocks |
|---|----------|--------|
| — | None | — |

## 9. Changes made while building

| Where | Change | Why |
|-------|--------|-----|
| A4, P6 | `AnswerQuestion` opens an `answer` span and passes its context explicitly to a child `answer generate` span inside the HybridCache factory | HybridCache runs the factory without the caller's ExecutionContext: the first live trace showed the embedding, SQL and `chat claude-sonnet-5-5` spans as separate root traces, not under `POST /ask` or `POST /mcp` |
| A4 | `SqlMigrationRunner` opens a `migrate` span; `IngestCorpus` opens `ingest` and one span per document; the Ingest host now starts and stops its host | OpenTelemetry's providers start in a hosted service, and the Ingest host never started; the migration queries were otherwise loose root spans |
| A5, `CLAUDE.md` | Connection strings use `Host=127.0.0.1`, not `localhost` | With `localhost`, Npgsql tries `::1` first; the container listens on 127.0.0.1 only, and on Windows the refused IPv6 attempt took about 2 s. That is the readiness check's whole 2 s budget, so `/health/ready` reported Postgres down although it was up (measured: 2.0 s per check → 5 ms) |
| P3 | `AuthOptions` validates `Auth:ApiKey` with `[Required]` and `[MinLength(32)]` and `ValidateOnStart`; an authorization fallback policy protects every endpoint that does not say `AllowAnonymous` | Fail closed: a new endpoint is protected by default |
| P2 | The SDK prefixes a tool error with "An error occurred invoking '<tool>': " before the `McpException` message, and leaves null properties out of structured content (REST returns them as `null`) | Observed with the SDK's client; no change needed |
| P4 | `Retry-After` is the full window (60 s) rather than the time left in it: `FixedWindowRateLimiter` reports its window as the `RetryAfter` metadata | It never tells a caller to retry too early |
| A2 | `scripts/mcp-check.cs` (a .NET 10 file-based app) lists and calls the tools as the Day 2 agent will, with the key from `REGLENS_API_KEY`. `ModelContextProtocol` 2.4.0 is in `Directory.Packages.props` for it | The MCP Inspector was not needed |
| R6.15 | `IOllamaApiClient` is registered (the concrete client once, two registrations) so the readiness check can list models; the embedding generator is wrapped with `UseOpenTelemetry` | The health check needs the client, the trace needs the wrapper |

## 10. Results (2026-11-24)

- **A1:** `dotnet build`: 0 warnings. `dotnet test`: **285 passed** (was 235). New: API key (accepted; 401 on `/search`, `/passages`, `/ask`, `/mcp` without or with a wrong key; startup refused without a key or with a short one), `GET /passages/{id}` (200/404/400), health (Healthy, Degraded without Redis, 503 without Postgres or Ollama, live stays 200), rate limits (429 with `Retry-After` on the 3rd ask at a limit of 2; REST asks use up the MCP ask; `tools/list` not counted), MCP through the SDK's client in-process (three tools with annotations and output schemas for a `2025-11-25` and a `2026-07-28` client; structured results; tool errors for bad input; no key, no connection), the health checks themselves, and the read-only login (Testcontainers: reads all tables and the three BM25 views; INSERT, UPDATE, DELETE, TRUNCATE, REFRESH and CREATE TABLE refused with 42501; tables created later are readable; the migration runs in a second database after the role exists).
- **A2:** against the real index, `scripts/mcp-check.cs` negotiated protocol `2026-07-28` and listed the three tools with their annotations and schemas. `search_regulations` "personal data breach intimation to the Board" ranked Rule 7 first (dense 1, keyword 1, similarity 0.776); `get_passage 1966` returned its full text; `get_passage 999999` was a tool error "No passage with id 999999."
- **A3:** `ask_regulations` rbc-03 answered from the answer cache with three verified quotes, Para 100 (8:00 a.m.–7:00 p.m., not for microfinance) and Para 91 (microfinance, 9:00 a.m.–6:00 p.m.). out-01 (GST on gold jewellery) abstained (`model-abstained`): 3,552 in / 137 out.
- **A4:** one `POST /ask` (uncached) in the Aspire dashboard, 3.49 s: `answer` → `answer generate` → `embeddings nomic-embed-text` 0.14 s (Ollama) → two `postgresql` 39 ms and 10 ms → `chat claude-sonnet-5-5` 2.84 s (Anthropic HTTP 2.58 s), with `gen_ai.usage.input_tokens` 4,398, `output_tokens` 271, and the prompt and answer text (D62). Ingest: a `migrate` trace (13 spans) and an `ingest` trace with one span per document.
- **A5:** the API ran as `reglens_api` after `scripts/create-api-db-user.ps1` (`pg_stat_activity`). `/health/ready` Healthy; with Redis stopped, Degraded 200 and search still worked; Healthy again after Redis restarted. Six identical `POST /ask` within a minute: 200 ×5 (four from the cache), then 429 with `Retry-After: 60`. A `Host: evil.example` request got 400.
- **A6, spend:** $0.0085 (out-01) + $0.0115 (the uncached `/ask`) = **about $0.02** of the $0.50 cap; every other answer came from the cache.

## 11. Sources (checked 2026-11-24)

- `ModelContextProtocol.AspNetCore` 2.4.0, released 2026-11-07: https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/2.4.0.
  The SDK docs at tag `v2.4.0` (https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.4.0/docs/concepts):
  - `getting-started.md`: stateless example and `AllowedHosts` guidance.
  - `stateless/stateless.md`: stateless is the default, and `initialize` clients are still served.
  - `tools/tools.md`: `UseStructuredContent`, and `McpException` → `isError`.
  - `identity/identity.md`: `ClaimsPrincipal` reaches tools.
  - `transports/transports.md`
  - `HttpServerSessionMode.cs`
  - `McpEndpointRouteBuilderExtensions.cs`: `MapMcp` returns `IEndpointConventionBuilder`.
  - `McpServerToolAttribute.cs`: `ReadOnly`, `Idempotent`, `OpenWorld`, `Title`.
  - `Diagnostics.cs`: ActivitySource and Meter `Experimental.ModelContextProtocol`.
  - Rendered: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html
- MCP Python SDK v2.3.0 (`src/mcp/client/streamable_http.py`, `shared/_httpx_utils.py`): `streamable_http_client`
  takes an `httpx` client, which `create_mcp_http_client(headers=…)` builds. This is how the Day 2 agent sends
  `X-API-Key`.
- OpenTelemetry .NET 1.21.0 (`OpenTelemetry.Extensions.Hosting`, `.Exporter.OpenTelemetryProtocol`,
  `.Instrumentation.AspNetCore`, `.Instrumentation.Http`, `.Instrumentation.Runtime`), NuGet. The exporter README at
  `core-1.21.0` covers `UseOtlpExporter`: once only, and it enables logs, metrics and traces.
- `Npgsql.OpenTelemetry` 10.0.4 (matches Npgsql 10.0.4): https://www.nuget.org/packages/Npgsql.OpenTelemetry/10.0.4,
  and https://www.npgsql.org/doc/diagnostics/tracing.html (`AddNpgsql()`, `ConfigureTracing`).
- `Microsoft.Extensions.AI` 10.11.1: https://www.nuget.org/packages/Microsoft.Extensions.AI/10.11.1. From the source
  at tag `v10.11.1`:
  - `OpenTelemetryConsts.cs`: `DefaultSourceName = "Experimental.Microsoft.Extensions.AI"`.
  - `OpenTelemetryChatClient.cs`: GenAI semantic conventions v1.41, `EnableSensitiveData` default false, or the
    `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` env var.
- Aspire dashboard configuration: https://aspire.dev/dashboard/configuration/ (`Dashboard:Otlp:AuthMode` default
  Unsecured) and https://aspire.dev/dashboard/standalone/
- ASP.NET Core:
  - Rate limiting: https://learn.microsoft.com/aspnet/core/performance/rate-limit
  - Health checks: https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks
  - Host filtering: https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/host-filtering
  - Authentication: https://learn.microsoft.com/aspnet/core/security/authentication/
- `CryptographicOperations.FixedTimeEquals`:
  https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals
- PostgreSQL 17 `GRANT` (`ALL TABLES` covers views and foreign tables; materialized views are not mentioned) and
  `ALTER DEFAULT PRIVILEGES`: https://www.postgresql.org/docs/17/sql-grant.html,
  https://www.postgresql.org/docs/17/sql-alterdefaultprivileges.html
- OWASP Top 10 for LLM Applications (LLM01, prompt injection):
  https://owasp.org/www-project-top-10-for-large-language-model-applications/
- Not verified: how the MCP Inspector renders output schemas. A2 accepts a client script instead.
