using System.Globalization;
using System.Text.RegularExpressions;
using RegLens.Domain.Chunking;

namespace RegLens.Domain.Search;

/// <summary>Finds citations in a free-text query: "para 6", "Rule 7", "item 4", "annex I", "first schedule" (spec 0002, R2.2).</summary>
public static partial class CitationQuery
{
    public static IReadOnlyList<Citation> Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var citations = new List<Citation>();
        foreach (Match match in Numbered().Matches(query))
        {
            var kind = match.Groups["kind"].Value.ToLowerInvariant();
            var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
            citations.Add(kind switch
            {
                "rule" => new Citation($"Rule {number}", CitationMatch.Prefix),
                "item" => new Citation($"Item {number}", CitationMatch.PrefixOrSuffix),
                _ => new Citation($"Para {number}", CitationMatch.Prefix),
            });
        }

        foreach (Match match in Annex().Matches(query))
        {
            if (AnnexNumeral(match.Groups["n"].Value) is { } numeral)
            {
                citations.Add(new Citation($"Annex {numeral}", CitationMatch.Prefix));
            }
        }

        foreach (Match match in Schedule().Matches(query))
        {
            var ordinal = match.Groups["ord"].Value.ToLowerInvariant();
            citations.Add(new Citation($"{char.ToUpperInvariant(ordinal[0])}{ordinal[1..]} Schedule", CitationMatch.Prefix));
        }

        return citations.Distinct().ToList();
    }

    // "Annex 1" and "Annex I" both mean the first annex; chunk references use Roman numerals.
    private static string? AnnexNumeral(string text)
    {
        var upper = text.ToUpperInvariant();
        if (RomanNumeral.ToInt(upper) is not null)
        {
            return upper;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 20
            ? ToRoman(n)
            : null;
    }

    private static string ToRoman(int n)
    {
        (int Value, string Symbol)[] map = [(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var result = "";
        foreach (var (value, symbol) in map)
        {
            while (n >= value)
            {
                result += symbol;
                n -= value;
            }
        }

        return result;
    }

    [GeneratedRegex(@"\b(?<kind>para(?:graph)?|rule|item)\.?\s*(?<n>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Numbered();

    [GeneratedRegex(@"\bannex(?:ure)?\s*[–—-]?\s*(?<n>[ivx]{1,5}|\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Annex();

    [GeneratedRegex(@"\b(?<ord>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth)\s+schedule\b", RegexOptions.IgnoreCase)]
    private static partial Regex Schedule();
}
