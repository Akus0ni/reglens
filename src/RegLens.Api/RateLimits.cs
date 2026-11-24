using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace RegLens.Api;

/// <summary>Requests per caller per minute (spec 0006, D59). Section "RateLimits".</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary><c>POST /ask</c> and <c>ask_regulations</c>: each may call Claude.</summary>
    [Range(1, 10_000)]
    public int AskPerMinute { get; set; } = 5;

    /// <summary><c>GET /search</c>, <c>GET /passages/{id}</c>, <c>search_regulations</c> and <c>get_passage</c>.</summary>
    [Range(1, 10_000)]
    public int ReadPerMinute { get; set; } = 30;
}

public enum RateLimitBucket
{
    Ask,
    Read,
}

/// <summary>
/// One fixed one-minute window per caller and bucket, shared by the REST endpoints and the MCP tools (spec 0006,
/// R6.11, P4). The built-in middleware keeps a counter per policy and sees <c>/mcp</c> as one route, so a filter on
/// each side acquires from these limiters instead.
/// </summary>
public sealed class RateLimitBuckets : IDisposable
{
    private readonly PartitionedRateLimiter<string> _ask;
    private readonly PartitionedRateLimiter<string> _read;

    public RateLimitBuckets(IOptions<RateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ask = Create(options.Value.AskPerMinute);
        _read = Create(options.Value.ReadPerMinute);
    }

    /// <summary>Takes one permit, or returns how long to wait for the next window.</summary>
    public RateLimitDecision TryAcquire(RateLimitBucket bucket, string caller)
    {
        using var lease = (bucket == RateLimitBucket.Ask ? _ask : _read).AttemptAcquire(caller);
        if (lease.IsAcquired)
        {
            return RateLimitDecision.Allowed;
        }

        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
        return new RateLimitDecision(false, retryAfter);
    }

    /// <summary>The authenticated caller, or the remote address if there is none.</summary>
    public static string Caller(ClaimsPrincipal? user, HttpContext? http) =>
        user?.Identity is { IsAuthenticated: true, Name: { } name }
            ? name
            : http?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public void Dispose()
    {
        _ask.Dispose();
        _read.Dispose();
    }

    private static PartitionedRateLimiter<string> Create(int permits) =>
        PartitionedRateLimiter.Create<string, string>(caller => RateLimitPartition.GetFixedWindowLimiter(caller, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}

public readonly record struct RateLimitDecision(bool IsAllowed, TimeSpan RetryAfter)
{
    public static RateLimitDecision Allowed { get; } = new(true, TimeSpan.Zero);

    /// <summary>Whole seconds, at least 1, for <c>Retry-After</c> and the MCP message.</summary>
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));
}

/// <summary>REST side of the limits: 429 with <c>Retry-After</c> (spec 0006, R6.11).</summary>
public sealed class RateLimitFilter(RateLimitBucket bucket) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var decision = http.RequestServices.GetRequiredService<RateLimitBuckets>()
            .TryAcquire(bucket, RateLimitBuckets.Caller(http.User, http));
        if (decision.IsAllowed)
        {
            return await next(context).ConfigureAwait(false);
        }

        http.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return TypedResults.Problem(
            $"Rate limit exceeded. Retry after {decision.RetryAfterSeconds} s.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }
}

public static class RateLimitEndpointExtensions
{
    public static TBuilder WithRateLimit<TBuilder>(this TBuilder builder, RateLimitBucket bucket)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new RateLimitFilter(bucket));
}
