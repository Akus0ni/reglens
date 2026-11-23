using System.Text.Json;
using System.Text.Json.Serialization;

namespace RegLens.Evals;

/// <summary>An expected source: references within one document, matched by <see cref="GoldenSet.Matches"/>.</summary>
public sealed record ExpectedSource(string Document, IReadOnlyList<string> Refs);

public sealed record GoldenQuestion(
    string Id,
    bool Answerable,
    string Kind,
    string Question,
    IReadOnlyList<ExpectedSource>? Expected = null,
    string? Notes = null);

/// <summary>The owner-reviewed questions in <c>evals/golden-set.json</c> (spec 0004, D42, R4.7).</summary>
public sealed record GoldenSet(int Version, string Created, string Status, string Matching, IReadOnlyList<GoldenQuestion> Questions)
{
    public const int AnswerableCount = 20;
    public const int UnanswerableCount = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public IEnumerable<GoldenQuestion> Answerable => Questions.Where(q => q.Answerable);

    public IEnumerable<GoldenQuestion> Unanswerable => Questions.Where(q => !q.Answerable);

    public static GoldenSet Load(string path)
    {
        var set = JsonSerializer.Deserialize<GoldenSet>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        var errors = set.Validate();
        return errors.Count == 0 ? set : throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    /// <summary>The shape rules of R4.7. Whether each reference exists in the index is checked against the database.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var duplicates = Questions.GroupBy(q => q.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        errors.AddRange(duplicates.Select(id => $"Duplicate id {id}."));
        if (Answerable.Count() != AnswerableCount || Unanswerable.Count() != UnanswerableCount)
        {
            errors.Add($"Expected {AnswerableCount} answerable and {UnanswerableCount} unanswerable questions, found {Answerable.Count()} and {Unanswerable.Count()}.");
        }

        foreach (var question in Questions)
        {
            var refs = question.Expected?.Sum(e => e.Refs.Count) ?? 0;
            if (question.Answerable && refs == 0)
            {
                errors.Add($"{question.Id} is answerable but names no expected reference.");
            }
            else if (!question.Answerable && refs > 0)
            {
                errors.Add($"{question.Id} is unanswerable but names expected references.");
            }

            if (string.IsNullOrWhiteSpace(question.Question))
            {
                errors.Add($"{question.Id} has no question text.");
            }
        }

        return errors;
    }

    /// <summary>
    /// A chunk matches a reference when its <c>para_ref</c> equals it or starts with it followed by ','
    /// ("Para 13" matches "Para 13, part 1 of 2", never "Para 130"), within the named document.
    /// </summary>
    public static bool Matches(GoldenQuestion question, string documentId, string paraRef)
    {
        ArgumentNullException.ThrowIfNull(question);
        return question.Expected?.Any(e => e.Document == documentId && e.Refs.Any(r => Matches(r, paraRef))) == true;
    }

    public static bool Matches(string reference, string paraRef)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(paraRef);
        return paraRef.Equals(reference, StringComparison.Ordinal) || paraRef.StartsWith(reference + ",", StringComparison.Ordinal);
    }
}
