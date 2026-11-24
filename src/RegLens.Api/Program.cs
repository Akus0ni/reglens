using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RegLens.Api;
using RegLens.Application;
using RegLens.Infrastructure;
using RegLens.Infrastructure.Health;
using RegLens.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
// A value that cannot bind (topK=abc) is the caller's mistake: 400, not a Development-only exception and 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddRegLensApplication().AddRegLensInfrastructure(builder.Configuration).AddRegLensAnswers(builder.Configuration);
builder.Services.AddHostedService<EmbeddingWarmup>();

// Spec 0006. Every endpoint needs the API key unless it says otherwise (fallback policy): fail closed (D58).
builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder(ApiKeyAuthenticationHandler.SchemeName).RequireAuthenticatedUser().Build());

builder.Services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<RateLimitBuckets>();

// Readiness (D61, R6.12): Postgres and Ollama are required, Redis only degrades the answer cache.
var checkTimeout = TimeSpan.FromSeconds(2);
string[] ready = ["ready"];
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", HealthStatus.Unhealthy, ready, checkTimeout)
    .AddCheck<OllamaHealthCheck>("ollama", HealthStatus.Unhealthy, ready, checkTimeout)
    .AddCheck<RedisHealthCheck>("redis", HealthStatus.Degraded, ready, checkTimeout);

// MCP (D14, R6.1, P1): stateless Streamable HTTP; tools/call goes through the shared limits (R6.11, P4).
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        var bucket = context.Params?.Name == "ask_regulations" ? RateLimitBucket.Ask : RateLimitBucket.Read;
        var limits = context.Services!.GetRequiredService<RateLimitBuckets>();
        var decision = limits.TryAcquire(bucket, RateLimitBuckets.Caller(context.User, null));
        return decision.IsAllowed
            ? await next(context, cancellationToken).ConfigureAwait(false)
            : new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Rate limit exceeded. Retry after {decision.RetryAfterSeconds} s." }],
            };
    }))
    // The SDK drops null properties by default (WhenWritingNull) although the generated output schema lists them as
    // required; clients that validate structuredContent (the Python MCP SDK does) then reject the result. Keep nulls.
    .WithTools<RegulationTools>(new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    });

builder.Services.AddRegLensTelemetry(builder.Configuration, "reglens-api")
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapSearchEndpoints();
app.MapPassageEndpoints();
app.MapAskEndpoints();
app.MapMcp("/mcp").RequireAuthorization();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Visible to WebApplicationFactory in the API tests.</summary>
public partial class Program;
