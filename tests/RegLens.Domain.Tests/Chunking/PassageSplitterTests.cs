using RegLens.Domain.Chunking;

namespace RegLens.Domain.Tests.Chunking;

public class PassageSplitterTests
{
    private const int MaxTokens = 100;
    private readonly PassageSplitter _splitter = new(MaxTokens);

    [Fact]
    public void LeavesAPassageUnderTheLimitAlone()
    {
        var passage = Passage("4. Short paragraph.", "(1) One clause.");

        _splitter.Split(passage).Should().ContainSingle().Which.Should().BeSameAs(passage);
    }

    [Fact]
    public void SplitsALongPassageIntoPartsWithinTheLimit()
    {
        var parts = _splitter.Split(LongDefinitions());

        parts.Should().HaveCountGreaterThan(1);
        parts.Should().AllSatisfy(p => TokenEstimate.Of(p.Content).Should().BeLessThanOrEqualTo(MaxTokens));
    }

    [Fact]
    public void StartsEveryLaterPartAtASubClauseAndRepeatsTheOpeningLine()
    {
        var parts = _splitter.Split(LongDefinitions());

        parts.Skip(1).Should().AllSatisfy(p =>
        {
            p.Lead.Should().Be("4. Definitions");
            p.Lines[0].Text.Should().MatchRegex(@"^\([ivx]+\)");
        });
    }

    [Fact]
    public void NumbersThePartsInTheParagraphReference()
    {
        var parts = _splitter.Split(LongDefinitions());

        parts.Select(p => p.ParaRef).Should().StartWith($"Para 4, part 1 of {parts.Count}");
        parts[^1].ParaRef.Should().Be($"Para 4, part {parts.Count} of {parts.Count}");
    }

    [Fact]
    public void KeepsEveryLineExactlyOnce()
    {
        var passage = LongDefinitions();

        _splitter.Split(passage).SelectMany(p => p.Lines).Should().Equal(passage.Lines);
    }

    [Fact]
    public void CutsASingleOverlongLineAtWordBoundaries()
    {
        var words = string.Join(' ', Enumerable.Repeat("verification", 80));
        var parts = _splitter.Split(Passage("4. Definitions", words));

        parts.Should().AllSatisfy(p => TokenEstimate.Of(p.Content).Should().BeLessThanOrEqualTo(MaxTokens));
        parts.SelectMany(p => p.Lines).Skip(1).Should().AllSatisfy(l => l.Text.Should().NotStartWith(" "));
    }

    private static Passage LongDefinitions() => Passage(
        ["4. Definitions"],
        Enumerable.Range(1, 12).Select(i =>
            $"({Roman(i)}) ‘Term {i}’ means a defined expression used in these Directions for the purpose of clause {i}."));

    private static Passage Passage(params string[] lines) => Passage(lines.Take(1), lines.Skip(1));

    private static Passage Passage(IEnumerable<string> head, IEnumerable<string> rest) =>
        new("Para 4", "Chapter I – Preliminary", head.Concat(rest).Select(t => new PageLine(6, t)).ToList());

    private static string Roman(int n) => n switch
    {
        1 => "i", 2 => "ii", 3 => "iii", 4 => "iv", 5 => "v", 6 => "vi",
        7 => "vii", 8 => "viii", 9 => "ix", 10 => "x", 11 => "xi", _ => "xii",
    };
}
