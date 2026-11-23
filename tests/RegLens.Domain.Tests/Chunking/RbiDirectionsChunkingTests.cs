using RegLens.Domain.Chunking;

namespace RegLens.Domain.Tests.Chunking;

// Paragraph and chapter numbers in the H1–H5 cases are lowered (97 → 1, IV → III) because the first one must be within
// StructureMarkers.MaxNumberGap of 0; the text is otherwise the real PDF lines (spec 0005 §1).
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
    public void RecognisesAnAnnexWithItsTitleOnTheSameLine()
    {
        var passages = Segment("""
            1. The NBFC shall provide a KFS as per the format in Annex I.
            Annex I – Format of Key Facts Statement (KFS)
            Part 1 (Interest rate and fees / charges)
            1 Loan proposal/ account No. Type of Loan
            2 Sanctioned Loan amount (in Rupees)
            3 Disbursal schedule (i) Disbursement in stages or 100% upfront.
            4 Loan term (year/months/days)
            Annex II – Lending against collateral of Gold Jewellery [Deleted]
            """);

        // Annex II has only its title, so it folds into its heading and yields no chunk (spec 0001 §9).
        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Annex I");
        passages[1].Content.Should().Contain("Sanctioned Loan amount").And.NotContain("Gold Jewellery");
        passages[0].Content.Should().NotContain("Loan proposal");
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
    public void RecognisesSectionLettersAfterH() // H1
    {
        var passages = Segment("""
            Chapter III – Responsible Lending Conduct
            H. Conduct towards Microfinance borrowers
            H.5 Engagement of recovery agents for recovery of microfinance loans
            1. The up-to-date details of the recovery agencies shall be hosted on the website.
            I. Responsibilities of Direct Sales Agents (DSA) / Direct Marketing Agents (DMA)
            / Recovery Agents of the NBFC
            2. The NBFC shall ensure that the DSA / DMA / recovery agents are properly trained.
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Para 2");
        passages[0].Content.Should().NotContain("Responsibilities");
        passages[1].Heading.Should().Be("Chapter III – Responsible Lending Conduct › I. Responsibilities of Direct Sales "
            + "Agents (DSA) / Direct Marketing Agents (DMA) / Recovery Agents of the NBFC");
    }

    [Fact]
    public void JoinsAHeadingWrappedOverThreeLines() // H2
    {
        var passages = Segment("""
            Chapter III – Miscellaneous
            D. Compliance with Hon’ble Supreme Court Order dated April 30, 2025 in the
            matter of Pragya Prasun and Ors. vs Union of India and Ors. (W.P.(C) 289 of
            2024) and Amar Jain vs Union of India and Ors. (W.P.(C) 49 of 2025)
            1.Attention of the NBFC is drawn to the Order of the Hon’ble Supreme Court.
            """);

        passages.Should().ContainSingle().Which.Should().Match<Passage>(p => p.ParaRef == "Para 1");
        passages[0].Heading.Should().StartWith("Chapter III – Miscellaneous › D. Compliance with Hon’ble Supreme Court")
            .And.EndWith("(W.P.(C) 49 of 2025)");
    }

    [Theory]
    [InlineData("matter of the order", "ends with \"in the\"")]
    [InlineData("/ Recovery Agents", "next line starts with \"/\"")]
    public void JoinsTheNextLineOnlyWhileTheHeadingIsUnfinished(string next, string because)
    {
        var passages = Segment($"""
            Chapter III – Miscellaneous
            D. Compliance with the Supreme Court Order in the
            {next}
            1. The NBFC shall comply.
            """);

        passages.Should().ContainSingle().Which.Heading.Should().EndWith(next, because);
    }

    [Fact]
    public void DoesNotJoinALineThatStartsAParagraph()
    {
        var passages = Segment("""
            Chapter III – Miscellaneous
            D. Compliance with the Supreme Court Order in the
            1. The NBFC shall comply.
            """);

        passages.Should().ContainSingle().Which.Should().Match<Passage>(p =>
            p.ParaRef == "Para 1" && p.Heading == "Chapter III – Miscellaneous › D. Compliance with the Supreme Court Order in the");
    }

    [Fact]
    public void TreatsSectionHeadingsBeforeTheFirstChapterAsPreamble() // H3
    {
        var passages = Segment("""
            Table of Contents
            I. Responsibilities of Direct Sales Agents (DSA) / Direct Marketing Agents (DMA)
            D. Compliance with Hon’ble Supreme Court Order dated April 30, 2025 in the
            matter of Pragya Prasun and Ors. vs Union of India and Ors. (W.P.(C) 289 of 2024)
            In exercise of the powers conferred by sections 45JA, 45L and 45M of the Reserve
            Chapter I – Preliminary
            A. Short Title and Commencement
            1. These Directions shall be called the Reserve Bank of India Directions.
            """);

        passages.Select(p => p.ParaRef).Should().Equal(NumberedStructureChunking.PreambleRef, "Para 1");
        passages[0].Content.Should().Contain("In exercise of the powers conferred").And.Contain("D. Compliance");
        passages[1].Heading.Should().Be("Chapter I – Preliminary › A. Short Title and Commencement");
    }

    [Fact]
    public void StartsAParagraphInsertedByAmendment() // H4
    {
        var passages = Segment("""
            Chapter III – Miscellaneous
            1.Attention of the NBFC is drawn to the Order.
            E. Measures in case of declaration of calamity
            1A. An NBFC at its discretion, may provide relief measures.
            1B. A second inserted paragraph.
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Para 1A", "Para 1B");
        passages[1].Heading.Should().Be("Chapter III – Miscellaneous › E. Measures in case of declaration of calamity");
    }

    [Theory]
    [InlineData("2A. Not after 1.")]
    [InlineData("1A. Not twice.")]
    public void KeepsALetteredNumberThatDoesNotFollowItsBaseInsideTheParagraph(string line)
    {
        var passages = Segment($"""
            1. The NBFC shall apply a Risk Based Approach.
            1A. Inserted.
            {line}
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Para 1", "Para 1A");
        passages[1].Content.Should().Contain(line);
    }

    [Fact]
    public void StartsALetteredParagraphInsideAnAnnex() // H5
    {
        var passages = Segment("""
            Annex – I
            1. Regarding procedure for unfreezing of funds, financial assets or economic resources.
            1.1 Any individual or entity, if it has evidence to prove that the freezing.
            1A. Regarding procedure for unfreezing of funds, financial assets or economic resources of persons.
            """);

        passages.Select(p => p.ParaRef).Should().Equal("Annex I, Para 1", "Annex I, Para 1A");
        passages[0].Content.Should().NotContain("1A.");
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
