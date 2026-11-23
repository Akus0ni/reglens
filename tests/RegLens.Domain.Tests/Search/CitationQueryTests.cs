using RegLens.Domain.Search;

namespace RegLens.Domain.Tests.Search;

public class CitationQueryTests
{
    [Theory]
    [InlineData("Rule 7", "Rule 7")]
    [InlineData("what does rule 7 say?", "Rule 7")]
    [InlineData("para 6 digital lending", "Para 6")]
    [InlineData("Paragraph 13 on data", "Para 13")]
    [InlineData("Para 6(2)", "Para 6")]
    [InlineData("Item 4", "Item 4")]
    [InlineData("annex I", "Annex I")]
    [InlineData("Annex 2", "Annex II")]
    [InlineData("Annex – III", "Annex III")]
    [InlineData("the FIRST SCHEDULE", "First Schedule")]
    public void FindsCitationsInTheirChunkReferenceForm(string query, string reference) =>
        CitationQuery.Parse(query).Select(c => c.Reference).Should().ContainSingle().Which.Should().Be(reference);

    [Theory]
    [InlineData("What is a Key Fact Statement?")]
    [InlineData("rules about KYC")]
    [InlineData("annexure")]
    public void FindsNothingWhenNoCitationIsNamed(string query) =>
        CitationQuery.Parse(query).Should().BeEmpty();

    [Fact]
    public void FindsSeveralCitationsOnceEach()
    {
        CitationQuery.Parse("Compare rule 7 with Rule 7 and para 9").Select(c => c.Reference)
            .Should().Equal("Rule 7", "Para 9");
    }

    [Theory]
    [InlineData("Para 6", true)]
    [InlineData("Para 6, part 2 of 3", true)]
    [InlineData("Para 60", false)]
    [InlineData("Para 61, part 1 of 2", false)]
    [InlineData("Annex I, Para 6", false)]
    public void MatchesAParagraphAndItsPartsOnly(string paraRef, bool matches) =>
        new Citation("Para 6", CitationMatch.Prefix).Matches(paraRef).Should().Be(matches);

    [Theory]
    [InlineData("Item 4", true)]
    [InlineData("First Schedule, Part A, Item 4", true)]
    [InlineData("First Schedule, Part A, Item 14", false)]
    public void MatchesScheduleItemsByTheirEnding(string paraRef, bool matches) =>
        new Citation("Item 4", CitationMatch.PrefixOrSuffix).Matches(paraRef).Should().Be(matches);
}
