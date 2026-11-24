using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using RegLens.Application.Answers;
using RegLens.Application.Search;

namespace RegLens.Api;

/// <summary>The body of <c>POST /ask</c> (spec 0003, D35).</summary>
public sealed record AskRequest(string? Question, int? TopK);

/// <summary><c>POST /ask</c> (spec 0003, R3.15–R3.16, P11–P12).</summary>
public static class AskEndpoints
{
    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ask", AskAsync).WithName("Ask").WithRateLimit(RateLimitBucket.Ask);
        return app;
    }

    private static async Task<Results<Ok<AnswerResult>, ValidationProblem, ProblemHttpResult>> AskAsync(
        AskRequest request,
        AnswerQuestion answers,
        IOptions<SearchOptions> options,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var question = request.Question?.Trim() ?? "";
        var k = request.TopK ?? settings.DefaultTopK;

        var errors = RequestValidation.Question(question, k, settings, "question", "question");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            return TypedResults.Ok(await answers.RunAsync(question, k, cancellationToken).ConfigureAwait(false));
        }
        catch (AnswerGenerationException exception)
        {
            return exception.Failure == AnswerFailure.ModelUnavailable
                ? TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable)
                : TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
