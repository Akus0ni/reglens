using RegLens.Domain.Chunking;

namespace RegLens.Domain.Tests.Chunking;

public class RbiDirectionsChunkingTests
{
    private readonly NumberedStructureChunking _chunking = new(StructureMarkers.RbiDirections);

    [Fact]
    public void StartsAPassageAtEachNumberedParagraphWithItsHeadingTrail()
    {
        var passages = Segment("""
            Chapter III - Digital Lending
            A. General Requirements for NBFC-LSP Arrangements
            1. An NBFC shall ensure that the LSP complies with these Directions.
            2. An NBFC shall conduct due diligence before entering into a partnership.
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Para 2");
        passages.Should().AllSatisfy(p =>
            p.Heading.Should().Be("Chapter III – Digital Lending › A. General Requirements for NBFC-LSP Arrangements"));
    }

    [Fact]
    public void AddsSubSectionsToTheTrail()
    {
        var passages = Segment("""
            Chapter I - Preliminary
            B. General Guidelines
            B.1 Phases of Projects
            70. A project shall be classified as under construction until it achieves DCCO.
            """);

        passages.Should().ContainSingle().Which.Heading
            .Should().Be("Chapter I – Preliminary › B. General Guidelines › B.1 Phases of Projects");
    }

    [Fact]
    public void IgnoresChapterHeadingsThatAreOutOfOrder()
    {
        // A wrapped table-of-contents entry for Chapter IX appears before Chapter I.
        var passages = Segment("""
            Chapter IX – Requirements / obligations under International Agreements -
            Chapter I – Preliminary
            1. These Directions shall be called the Reserve Bank of India Directions, 2025.
            """);

        passages[^1].Heading.Should().Be("Chapter I – Preliminary");
        passages.Should().NotContain(p => p.Heading.Contains("IX", StringComparison.Ordinal));
    }

    [Fact]
    public void FoldsAWrappedHeadingLineIntoTheHeading()
    {
        var passages = Segment("""
            Chapter I - Other Regulations applicable to Non-Banking Financial
            Companies
            1. The NBFC shall maintain the records.
            """);

        passages.Should().ContainSingle().Which.Heading
            .Should().Be("Chapter I – Other Regulations applicable to Non-Banking Financial Companies");
    }

    [Fact]
    public void KeepsStrayNumberedLinesInsideTheirParagraph()
    {
        var passages = Segment("""
            1. These Directions shall be called the Reserve Bank of India Directions.
            2. These Directions shall come into effect immediately.
            15. All concerned are requested to ensure strict compliance.
            1. Sr. PPS to HS
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Para 2");
        passages[^1].Content.Should().Contain("1. Sr. PPS to HS");
    }

    [Fact]
    public void RestartsNumberingInsideAnAnnex()
    {
        var passages = Segment("""
            Chapter I – Preliminary
            1. These Directions shall be called the Reserve Bank of India Directions.
            Annex – I
            ORDER
            Subject: Procedure for implementation of Section 51A of the Unlawful Activities (Prevention) Act, 1967.
            Section 51A reads as under.
            2. In order to ensure expeditious and effective implementation of the provisions.
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Annex I", "Annex I, Para 2");
    }

    [Fact]
    public void KeepsTextBeforeTheFirstParagraphAsThePreamble()
    {
        var passages = Segment("""
            RBI/DOR/2025-26/347
            Reserve Bank of India (Non-Banking Financial Companies – Credit Facilities) Directions, 2025
            Chapter I - Preliminary
            1. These Directions shall be called the Reserve Bank of India Directions.
            """);

        passages[0].ParaRef.Should().Be(NumberedStructureChunking.PreambleRef);
        passages[0].Content.Should().Contain("RBI/DOR/2025-26/347");
    }

    [Fact]
    public void RecordsTheFirstAndLastPageOfAPassage()
    {
        var passages = _chunking.Segment(
        [
            new PageLine(14, "9. Disclosures to borrowers"),
            new PageLine(14, "(1) An NBFC shall provide a Key Fact Statement."),
            new PageLine(15, "(2) The KFS shall be provided before the contract is executed."),
        ]);

        passages.Should().ContainSingle().Which.Should().Match<Passage>(p => p.PageStart == 14 && p.PageEnd == 15);
    }

    private IReadOnlyList<Passage> Segment(string text) => _chunking.Segment(
        text.Split('\n').Select(line => new PageLine(1, line.Trim())).Where(line => line.Text.Length > 0).ToList());
}
