using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Domain.Tests.Chunking;

public class TextCleanerTests
{
    [Fact]
    public void RemovesTableOfContentsLines()
    {
        var lines = Clean(Page(1, """
            Chapter I - Preliminary ............................................................................................... 5
            Chapter IX – Other Regulations applicable to Non-Banking Financial Companies ... 49
            1. These Directions shall be called the Reserve Bank of India Directions.
            """));

        lines.Should().ContainSingle().Which.Text.Should().StartWith("1. These Directions");
    }

    [Fact]
    public void RemovesStandalonePageNumbers()
    {
        Clean(Page(5, "    5 \n26. Customer due diligence."))
            .Select(l => l.Text).Should().Equal("26. Customer due diligence.");
    }

    [Fact]
    public void RemovesMostlyNonLatinLines()
    {
        Clean(Page(1, " भारतीय रिज़र्व बैंक\nRESERVE BANK OF INDIA"))
            .Select(l => l.Text).Should().Equal("RESERVE BANK OF INDIA");
    }

    [Fact]
    public void RemovesHeadersRepeatedOnManyPagesEvenWhenPageNumbersDiffer()
    {
        var pages = Enumerable.Range(24, 4)
            .Select(n => Page(n, $"{n}  THE GAZETTE OF INDIA : EXTRAORDINARY    [PART II—SEC. 3(i)]\nRule text on page {n}."))
            .ToList();

        TextCleaner.Clean(pages).Should().NotContain(l => l.Text.Contains("GAZETTE", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsParagraphAndClauseNumbersAndCollapsesSpaces()
    {
        Clean(Page(6, "3. \n(1) These  Directions shall apply:\n(i) NBFC-D registered with the RBI."))
            .Select(l => l.Text).Should().Equal("3.", "(1) These Directions shall apply:", "(i) NBFC-D registered with the RBI.");
    }

    [Fact]
    public void KeepsThePageOfEachLine()
    {
        TextCleaner.Clean([Page(7, "Line on seven."), Page(8, "Line on eight.")])
            .Select(l => l.Page).Should().Equal(7, 8);
    }

    private static IReadOnlyList<PageLine> Clean(PageText page) => TextCleaner.Clean([page]);

    private static PageText Page(int number, string text) => new(number, text);
}
