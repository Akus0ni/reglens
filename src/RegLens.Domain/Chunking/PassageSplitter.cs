using System.Globalization;
using System.Text.RegularExpressions;

namespace RegLens.Domain.Chunking;

/// <summary>
/// Splits passages above the token limit at sub-clause boundaries such as (1), (i) or (a) (spec 0001, R1.15).
/// Every part after the first repeats the passage's opening line, so each part stands alone.
/// A split never crosses into another paragraph, because it only ever works inside one passage.
/// </summary>
public sealed partial class PassageSplitter
{
    public const int DefaultMaxTokens = 500;

    private readonly int _maxTokens;

    public PassageSplitter(int maxTokens = DefaultMaxTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 50);
        _maxTokens = maxTokens;
    }

    public IReadOnlyList<Passage> Split(Passage passage)
    {
        ArgumentNullException.ThrowIfNull(passage);
        if (TokenEstimate.Of(passage.Content) <= _maxTokens)
        {
            return [passage];
        }

        var lead = passage.Lines[0].Text;
        var partBudget = _maxTokens - TokenEstimate.Of(lead + "\n");
        var lines = BreakLongLines(passage.Lines, partBudget);
        var parts = Pack(lines, firstBudget: _maxTokens, laterBudget: partBudget);

        return parts
            .Select((partLines, index) => new Passage(
                PartRef(passage.ParaRef, index + 1, parts.Count),
                passage.Heading,
                partLines,
                index == 0 ? "" : lead))
            .ToList();
    }

    private static List<List<PageLine>> Pack(List<PageLine> lines, int firstBudget, int laterBudget)
    {
        var parts = new List<List<PageLine>>();
        var current = new List<PageLine>();

        foreach (var line in lines)
        {
            var budget = parts.Count == 0 ? firstBudget : laterBudget;
            if (current.Count > 0 && Tokens(current, line) > budget)
            {
                current = CloseParts(parts, current, line, laterBudget);
            }

            current.Add(line);
        }

        parts.Add(current);
        return parts;
    }

    // Closes the full part at its last sub-clause start and returns the lines carried into the next part.
    // If the carried lines plus the next line would still not fit, they become a part of their own.
    private static List<PageLine> CloseParts(List<List<PageLine>> parts, List<PageLine> current, PageLine next, int laterBudget)
    {
        var cut = LastSubClauseStart(current);
        if (cut == 0)
        {
            parts.Add(current);
            return [];
        }

        parts.Add(current.GetRange(0, cut));
        var carried = current.GetRange(cut, current.Count - cut);
        if (Tokens(carried, next) <= laterBudget)
        {
            return carried;
        }

        parts.Add(carried);
        return [];
    }

    private static int Tokens(List<PageLine> lines, PageLine next) =>
        TokenEstimate.Of(string.Join('\n', lines.Select(l => l.Text).Append(next.Text)));

    private static int LastSubClauseStart(List<PageLine> lines)
    {
        for (var i = lines.Count - 1; i > 0; i--)
        {
            if (SubClauseStart().IsMatch(lines[i].Text))
            {
                return i;
            }
        }

        return 0;
    }

    // A single line longer than a part is cut at word boundaries.
    private static List<PageLine> BreakLongLines(IReadOnlyList<PageLine> lines, int budget)
    {
        var maxChars = budget * TokenEstimate.CharactersPerToken;
        var result = new List<PageLine>();
        foreach (var line in lines)
        {
            var text = line.Text;
            while (text.Length > maxChars)
            {
                var cut = text.LastIndexOf(' ', maxChars);
                cut = cut > 0 ? cut : maxChars;
                result.Add(line with { Text = text[..cut].TrimEnd() });
                text = text[cut..].TrimStart();
            }

            result.Add(line with { Text = text });
        }

        return result;
    }

    private static string PartRef(string paraRef, int part, int total) =>
        total == 1 ? paraRef : string.Create(CultureInfo.InvariantCulture, $"{paraRef}, part {part} of {total}");

    [GeneratedRegex(@"^\((\d{1,3}|[a-z]{1,2}|[ivxlc]{1,6})\)\s")]
    private static partial Regex SubClauseStart();
}
