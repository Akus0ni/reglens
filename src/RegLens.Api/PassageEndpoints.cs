using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RegLens.Application.Answers;

namespace RegLens.Api;

/// <summary><c>GET /passages/{id}</c>: one passage by chunk id, as MCP's <c>get_passage</c> (spec 0006, D63, R6.7, P8).</summary>
public static class PassageEndpoints
{
    public static IEndpointRouteBuilder MapPassageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/passages/{id:long}", GetAsync).WithName("GetPassage").WithRateLimit(RateLimitBucket.Read);
        return app;
    }

    private static async Task<Results<Ok<StoredPassage>, ValidationProblem, NotFound<ProblemDetails>>> GetAsync(
        long id,
        IPassageReader passages,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.ChunkId(id);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var found = await passages.GetAsync([id], cancellationToken).ConfigureAwait(false);
        return found.Count == 1
            ? TypedResults.Ok(found[0])
            : TypedResults.NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not Found", Detail = NotFoundMessage(id) });
    }

    internal static string NotFoundMessage(long id) => $"No passage with id {id}.";
}
