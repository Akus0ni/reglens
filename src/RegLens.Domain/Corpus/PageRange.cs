using System.Globalization;

namespace RegLens.Domain.Corpus;

/// <summary>An inclusive range of 1-based PDF page numbers.</summary>
public sealed record PageRange
{
    public PageRange(int first, int last)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
        First = first;
        Last = last;
    }

    public int First { get; }

    public int Last { get; }

    public bool Contains(int page) => page >= First && page <= Last;

    /// <summary>Parses "24-41" (or a single page "7").</summary>
    public static PageRange Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = text.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new FormatException($"Page range '{text}' is not in the form 'first-last'.");
        }

        var first = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var last = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : first;
        return new PageRange(first, last);
    }

    public override string ToString() => First == Last
        ? First.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{First}-{Last}");
}
