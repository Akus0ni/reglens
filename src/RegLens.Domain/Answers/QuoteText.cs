using System.Text;

namespace RegLens.Domain.Answers;

/// <summary>
/// The text form quotes are compared in (spec 0003, R3.3, D39): whitespace runs become one space, typographic quotes
/// and dashes become their ASCII forms. Case and words are kept exactly.
/// </summary>
public static class QuoteText
{
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(Fold(c));
        }

        return builder.ToString();
    }

    private static char Fold(char c) => c switch
    {
        '‘' or '’' or '‚' or '‛' => '\'',
        '“' or '”' or '„' or '‟' => '"',
        '‐' or '‒' or '–' or '—' or '−' => '-',
        _ => c,
    };
}
