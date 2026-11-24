# ADR 0002 — MCP as the boundary between RegLens and its agents

Status: Accepted (D1, D14; built in spec 0006) · Date: 2026-11-24

## Context

The compliance-review agent ([reglens-agent](https://github.com/Akus0ni/reglens-agent), Python) needs RegLens's search
and passages. Other clients (Claude Code, other agents) could use them too. The agent should only be able to read.

## Decision

RegLens exposes three read-only tools over MCP Streamable HTTP at `/mcp` (`ModelContextProtocol.AspNetCore` 2.4.0,
stateless): `search_regulations`, `get_passage`, `ask_regulations`. They adapt the same use cases and validation as the
REST endpoints, return structured content with output schemas, and are annotated `ReadOnly`. Access needs the API key
and shares the REST rate limits.

## Consequences

- Any MCP client discovers typed tools with their descriptions; the agent loads them with `langchain-mcp-adapters` and
  needs no RegLens-specific client code.
- The contract is the output schema, and both sides must honour it: the first live agent run failed because RegLens
  omitted null properties its own schema required, which the Python client validates and the C# client does not
  (fixed in reglens PR #21, with a test that checks every result against its schema).
- Stateless mode serves both the `initialize` handshake (`2025-11-25`, the agent's MCP 1.x client) and `2026-07-28`
  clients on one endpoint.
- Tool results are untrusted data for the calling model; every tool description says so, and the agent wraps them in
  delimiters.
- One trace spans both services: the agent's httpx instrumentation sends `traceparent`, and RegLens's ASP.NET Core
  spans join it in the Aspire dashboard.

## Alternatives considered

- **Call the REST API directly from the agent:** works, but every client writes its own client code and tool
  descriptions, and nothing advertises the contract.
- **Import RegLens as a library:** couples two languages and gives the agent write access to the store.
