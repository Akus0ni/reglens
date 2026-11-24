using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using RegLens.Application.Search;

namespace RegLens.Api;

/// <summary><c>GET /search</c> (spec 0002, R2.12–R2.13).</summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/search", SearchAsync).WithName("Search").WithRateLimit(RateLimitBucket.Read);
        return app;
    }

    private static async Task<Results<Ok<SearchResult>, ValidationProblem>> SearchAsync(
        string? q,
        int? topK,
        SearchRegulations search,
        IOptions<SearchOptions> options,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var question = q?.Trim() ?? "";
        var k = topK ?? settings.DefaultTopK;

        var errors = RequestValidation.Question(question, k, settings, "q", "query");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await search.RunAsync(question, k, cancellationToken).ConfigureAwait(false));
    }
}
