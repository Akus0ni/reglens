using RegLens.Domain.Chunking;

namespace RegLens.Domain.Tests.Chunking;

public class GazetteRulesChunkingTests
{
    private readonly NumberedStructureChunking _chunking = new(StructureMarkers.GazetteRules);

    [Fact]
    public void StartsAPassageAtEachRuleIncludingRulesWithNoSpaceAfterTheNumber()
    {
        var passages = Segment("""
            1. Short title and commencement. — (1) These rules may be called the Digital Personal Data Protection Rules, 2025.
            2.Definitions.— (1) In these rules, unless the context otherwise requires, —
            3. Notice given by Data Fiduciary to Data Principal. —The notice shall be clear.
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Rule 1", "Rule 2", "Rule 3");
    }

    [Fact]
    public void NamesScheduleItemsByScheduleAndPartWithTitlesInTheHeading()
    {
        var passages = Segment("""
            1. Short title and commencement. — (1) These rules may be called the Digital Personal Data Protection Rules, 2025.
            FIRST SCHEDULE
            [See rule 4]
            PART A
            Conditions for registration of Consent Manager
            1. The applicant is a company incorporated in India.
            2. The applicant has sufficient capacity, including technical, operational and financial capacity.
            PART B
            Obligations of Consent Manager
            1. The Consent Manager shall enable a Data Principal to give consent.
            """);

        passages.Select(p => p.ParaRef).Should().Equal(
            "Rule 1",
            "First Schedule, Part A, Item 1",
            "First Schedule, Part A, Item 2",
            "First Schedule, Part B, Item 1");
        passages[^1].Heading.Should().Be(
            "First Schedule — [See rule 4] › Part B — Obligations of Consent Manager");
    }

    [Fact]
    public void KeepsAScheduleWithoutNumberedItemsAsOnePassage()
    {
        var passages = Segment("""
            SECOND SCHEDULE
            [See rules 5(1) and 16]
            Standards for processing of personal data by State and its instrumentalities.
            (a) Processing is carried out in a lawful manner;
            (b) Processing is done for the uses specified in clause (b) of section 7 of the Act;
            """);

        passages.Should().ContainSingle().Which.ParaRef.Should().Be("Second Schedule");
    }

    private IReadOnlyList<Passage> Segment(string text) => _chunking.Segment(
        text.Split('\n').Select(line => new PageLine(24, line.Trim())).Where(line => line.Text.Length > 0).ToList());
}
