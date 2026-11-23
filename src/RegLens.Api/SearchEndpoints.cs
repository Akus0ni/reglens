using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using RegLens.Application.Search;

namespace RegLens.Api;

/// <summary><c>GET /search</c> (spec 0002, R2.12–R2.13).</summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/search", SearchAsync).WithName("Search");
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

        var errors = Validate(question, k, settings);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await search.RunAsync(question, k, cancellationToken).ConfigureAwait(false));
    }

    // Validation at the edge (spec 0002, P9): nothing invalid reaches the model or the database.
    private static Dictionary<string, string[]> Validate(string question, int topK, SearchOptions settings)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (question.Length == 0)
        {
            errors["q"] = ["The query is required."];
        }
        else if (question.Length > settings.MaxQueryLength)
        {
            errors["q"] = [$"The query must be at most {settings.MaxQueryLength} characters."];
        }

        if (topK < 1 || topK > settings.MaxTopK)
        {
            errors["topK"] = [$"topK must be between 1 and {settings.MaxTopK}."];
        }

        return errors;
    }
}
