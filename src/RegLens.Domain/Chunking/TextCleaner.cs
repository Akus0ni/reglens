using System.Text.RegularExpressions;
using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>
/// Removes extraction noise while keeping paragraph and clause numbers (spec 0001, R1.12):
/// table-of-contents lines, standalone page numbers, non-Latin lines and per-page headers.
/// </summary>
public static partial class TextCleaner
{
    private const int MinimumRepeatedPages = 3;
    private const double RepeatedPageShare = 0.3;
    private const int MinimumRepeatedLength = 8;

    public static IReadOnlyList<PageLine> Clean(IReadOnlyList<PageText> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var lines = pages
            .SelectMany(page => page.Text.Split('\n').Select(text => new PageLine(page.Number, Normalize(text))))
            .Where(line => line.Text.Length > 0)
            .ToList();
        var pageHeaders = FindRepeatedLines(lines, pages.Count);

        return lines
            .Where(line => !IsTableOfContentsEntry(line.Text)
                && !IsPageNumber(line.Text)
                && !IsMostlyNonLatin(line.Text)
                && !pageHeaders.Contains(Shape(line.Text)))
            .ToList();
    }

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    private static bool IsTableOfContentsEntry(string text) => DottedLeader().IsMatch(text);

    private static bool IsPageNumber(string text) => StandalonePageNumber().IsMatch(text);

    private static bool IsMostlyNonLatin(string text)
    {
        var letters = text.Count(char.IsLetter);
        if (letters == 0)
        {
            return text.Contains('�', StringComparison.Ordinal);
        }

        var latin = text.Count(c => char.IsLetter(c) && c <= 'ɏ');
        return latin < letters / 2.0;
    }

    // Gazette pages repeat a header that differs only by page number, so compare lines with digits masked.
    private static HashSet<string> FindRepeatedLines(List<PageLine> lines, int pageCount)
    {
        var threshold = Math.Max(MinimumRepeatedPages, (int)Math.Ceiling(pageCount * RepeatedPageShare));
        return lines
            .GroupBy(line => Shape(line.Text), StringComparer.Ordinal)
            .Where(group => group.Key.Length >= MinimumRepeatedLength
                && group.Select(line => line.Page).Distinct().Count() >= threshold)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Shape(string text) => Digits().Replace(text, "#");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\.{3,}\s*\d+$")]
    private static partial Regex DottedLeader();

    [GeneratedRegex(@"^\d{1,4}$")]
    private static partial Regex StandalonePageNumber();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
