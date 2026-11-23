using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Domain.Tests;

public class ValueObjectTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    public void EstimatesTokensAsCharactersDividedByFourRoundedUp(string text, int tokens) =>
        TokenEstimate.Of(text).Should().Be(tokens);

    [Fact]
    public void HashesDependOnDocumentParagraphAndContent()
    {
        var hash = ContentHash.Of("doc", "Para 1", "text");

        hash.Should().Be(ContentHash.Of("doc", "Para 1", "text"));
        hash.Value.Should().HaveLength(64);
        new[] { ContentHash.Of("other", "Para 1", "text"), ContentHash.Of("doc", "Para 2", "text"), ContentHash.Of("doc", "Para 1", "text!") }
            .Should().NotContain(hash);
    }

    [Theory]
    [InlineData("24-41", 24, 41)]
    [InlineData("7", 7, 7)]
    public void ParsesPageRanges(string text, int first, int last) =>
        PageRange.Parse(text).Should().Be(new PageRange(first, last));

    [Theory]
    [InlineData("41-24")]
    [InlineData("a-b")]
    [InlineData("0-3")]
    public void RejectsInvalidPageRanges(string text) =>
        FluentActions.Invoking(() => PageRange.Parse(text)).Should().Throw<Exception>();

    [Theory]
    [InlineData("I", 1)]
    [InlineData("IV", 4)]
    [InlineData("IX", 9)]
    [InlineData("XI", 11)]
    public void ReadsRomanChapterNumbers(string numeral, int value) =>
        RomanNumeral.ToInt(numeral).Should().Be(value);
}
