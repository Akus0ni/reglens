using RegLens.Application.Search;

namespace RegLens.Api;

/// <summary>
/// Validation at the edge, shared by the REST endpoints and the MCP tools so they cannot drift apart (spec 0002, P9;
/// spec 0006, R6.6, P1). Nothing invalid reaches the model or the database.
/// </summary>
public static class RequestValidation
{
    /// <summary>
    /// Errors by parameter name; empty when the text and <paramref name="topK"/> are valid. <paramref name="field"/> is
    /// the parameter (<c>q</c>, <c>question</c>, <c>query</c>), <paramref name="label"/> the word in the message.
    /// </summary>
    public static Dictionary<string, string[]> Question(string question, int topK, SearchOptions settings, string field, string label)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (question.Length == 0)
        {
            errors[field] = [$"The {label} is required."];
        }
        else if (question.Length > settings.MaxQueryLength)
        {
            errors[field] = [$"The {label} must be at most {settings.MaxQueryLength} characters."];
        }

        if (topK < 1 || topK > settings.MaxTopK)
        {
            errors["topK"] = [$"topK must be between 1 and {settings.MaxTopK}."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> ChunkId(long chunkId)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (chunkId < 1)
        {
            errors["chunkId"] = ["chunkId must be a positive passage id."];
        }

        return errors;
    }

    /// <summary>One line for an MCP tool error: "q: The query is required. topK: …".</summary>
    public static string Describe(Dictionary<string, string[]> errors) =>
        string.Join(" ", errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}"));
}
