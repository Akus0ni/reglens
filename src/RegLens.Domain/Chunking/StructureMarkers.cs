using System.Globalization;
using System.Text.RegularExpressions;
using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>
/// The line patterns that give a document type its structure (spec 0001, R1.13–R1.14).
/// Headings build the trail; a container (annex, schedule) restarts numbering; a part inside a container restarts it again.
/// </summary>
public sealed partial record StructureMarkers(
    DocumentStructure Structure,
    string UnitName,
    string ContainerItemName,
    IReadOnlyList<Func<string, string?>> Headings,
    Func<string, int?> ChapterNumber,
    Func<string, string?> Container,
    Func<string, string?> ContainerPart)
{
    /// <summary>How far a paragraph number may jump and still count as the next paragraph.</summary>
    public const int MaxNumberGap = 3;

    /// <summary>RBI Directions: "Chapter III – Digital Lending", "A. General Requirements …", "26.", "Annex – I", "Annex I – Title".</summary>
    public static StructureMarkers RbiDirections { get; } = new(
        DocumentStructure.RbiDirections,
        UnitName: "Para",
        ContainerItemName: "Para",
        Headings: [ChapterHeading, SectionHeading, SubSectionHeading],
        ChapterNumber: ChapterRoman,
        Container: AnnexLabel,
        ContainerPart: _ => null);

    /// <summary>Gazette rules: "17.Appointment of …", "FIRST SCHEDULE", "PART B".</summary>
    public static StructureMarkers GazetteRules { get; } = new(
        DocumentStructure.GazetteRules,
        UnitName: "Rule",
        ContainerItemName: "Item",
        Headings: [],
        ChapterNumber: _ => null,
        Container: ScheduleLabel,
        ContainerPart: PartLabel);

    /// <summary>The number that starts a paragraph or rule, if the line starts one.</summary>
    public static int? UnitNumber(string line)
    {
        var match = NumberedStart().Match(line);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static string? ChapterHeading(string line)
    {
        var match = Chapter().Match(line);
        return match.Success ? $"Chapter {match.Groups[1].Value} – {match.Groups[2].Value.Trim()}" : null;
    }

    private static int? ChapterRoman(string line)
    {
        var match = Chapter().Match(line);
        return match.Success ? RomanNumeral.ToInt(match.Groups[1].Value) : null;
    }

    private static string? SectionHeading(string line)
    {
        var match = Section().Match(line);
        return match.Success && !line.EndsWith('.') && !line.EndsWith(';') && !line.EndsWith(',')
            ? $"{match.Groups[1].Value}. {match.Groups[2].Value.Trim()}"
            : null;
    }

    private static string? SubSectionHeading(string line)
    {
        var match = SubSection().Match(line);
        return match.Success && !line.EndsWith('.') ? $"{match.Groups[1].Value} {match.Groups[2].Value.Trim()}" : null;
    }

    private static string? AnnexLabel(string line)
    {
        var match = Annex().Match(line);
        return match.Success ? $"Annex {match.Groups[1].Value}" : null;
    }

    private static string? ScheduleLabel(string line)
    {
        var match = Schedule().Match(line);
        return match.Success ? $"{TitleCase(match.Groups[1].Value)} Schedule" : null;
    }

    private static string? PartLabel(string line)
    {
        var match = Part().Match(line);
        return match.Success ? $"Part {match.Groups[1].Value}" : null;
    }

    private static string TitleCase(string word) =>
        string.Concat(word[..1].ToUpperInvariant(), word[1..].ToLowerInvariant());

    [GeneratedRegex(@"^(\d{1,3})\.(?:\s|$|(?=[A-Z“""‘(]))")]
    private static partial Regex NumberedStart();

    [GeneratedRegex(@"^Chapter\s*-?\s*([IVXLC]+)\s*[–—-]\s*(\S.*)$")]
    private static partial Regex Chapter();

    [GeneratedRegex(@"^([A-H])\.\s+([A-Z].{2,88})$")]
    private static partial Regex Section();

    [GeneratedRegex(@"^([A-H]\.\d{1,2})\s+([A-Z].{2,88})$")]
    private static partial Regex SubSection();

    // "Annex – I" alone, or with its title on the same line: "Annex I – Format of Key Facts Statement (KFS)".
    [GeneratedRegex(@"^Annex\s*[–—-]?\s*([IVXLC]+)(?:\s+[–—-]\s+\S.*)?$")]
    private static partial Regex Annex();

    [GeneratedRegex(@"^(FIRST|SECOND|THIRD|FOURTH|FIFTH|SIXTH|SEVENTH|EIGHTH|NINTH|TENTH)\s+SCHEDULE$")]
    private static partial Regex Schedule();

    [GeneratedRegex(@"^PART\s+([A-Z])$")]
    private static partial Regex Part();
}
