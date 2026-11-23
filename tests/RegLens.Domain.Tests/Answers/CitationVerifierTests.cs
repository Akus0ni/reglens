using RegLens.Domain.Answers;

namespace RegLens.Domain.Tests.Answers;

public class QuoteTextTests
{
    [Theory]
    [InlineData("  the  NBFC\nshall\tensure ", "the NBFC shall ensure")]
    [InlineData("the ‘lender’ and “borrower”", "the 'lender' and \"borrower\"")]
    [InlineData("on–line — off‒line − x‐y", "on-line - off-line - x-y")]
    [InlineData("Case Stays", "Case Stays")]
    public void FoldsWhitespaceQuotesAndDashes(string text, string expected) =>
        QuoteText.Normalise(text).Should().Be(expected);
}

public class CitationVerifierTests
{
    private const string Rule7 = """
        7. Intimation of personal data breach.—(1) On becoming aware of any personal data breach, the Data Fiduciary
        shall, to the best of its knowledge, intimate to each affected Data Principal, in a concise, clear and plain manner.
        """;

    private static readonly Dictionary<long, string> Passages = new()
    {
        [42] = Rule7,
        [7] = "6. The NBFC shall ensure that the LSP follows these Directions.",
        [8] = "8. Loans & advances to directors need board approval.",
    };

    [Fact]
    public void KeepsAQuoteThatIsInTheCitedPassage()
    {
        var check = Verify(new CitedQuote(42, "the Data Fiduciary shall, to the best of its knowledge"));

        check.Verified.Should().ContainSingle().Which.ChunkId.Should().Be(42);
        check.Dropped.Should().BeEmpty();
    }

    [Fact]
    public void RejectsAForgedChunkId()
    {
        var check = Verify(new CitedQuote(999, "the Data Fiduciary shall, to the best of its knowledge"));

        check.Verified.Should().BeEmpty();
        check.Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.UnknownChunk);
    }

    [Fact]
    public void RejectsARealIdWithAQuoteThatIsNotInThatPassage()
    {
        // The quote is real text, but from passage 42, not 7.
        var check = Verify(new CitedQuote(7, "the Data Fiduciary shall, to the best of its knowledge"));

        check.Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.QuoteNotFound);
    }

    [Fact]
    public void RejectsAParaphrase()
    {
        var check = Verify(new CitedQuote(42, "the Data Fiduciary must, as far as it knows, inform"));

        check.Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.QuoteNotFound);
    }

    [Fact]
    public void RejectsAQuoteThatDiffersOnlyInCase()
    {
        var check = Verify(new CitedQuote(42, "THE DATA FIDUCIARY SHALL, to the best of its knowledge"));

        check.Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.QuoteNotFound);
    }

    [Fact]
    public void RejectsQuotesShorterThanTheMinimum()
    {
        var check = Verify(new CitedQuote(7, "The NBFC shall"));

        check.Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.QuoteTooShort);
    }

    [Fact]
    public void ToleratesLineBreaksTypographicDashesAndEscapedCharacters()
    {
        var check = Verify(
            new CitedQuote(42, "Intimation of personal data breach.-(1) On becoming aware"),
            new CitedQuote(42, "the Data Fiduciary shall, to the best of its knowledge, intimate"));

        check.Verified.Should().HaveCount(2);
        // Passages are sent with & < > escaped; the model may copy the escaped form.
        Verify(new CitedQuote(8, "Loans &amp; advances to directors")).Verified.Should().ContainSingle();
    }

    [Fact]
    public void KeepsADuplicateOnceAndAtMostTheMaximum()
    {
        var quote = new CitedQuote(42, "the Data Fiduciary shall, to the best of its knowledge");
        var check = Verify(quote, quote);
        check.Verified.Should().ContainSingle();

        var many = Enumerable.Range(0, 12)
            .Select(i => new CitedQuote(42, Rule7.Replace("\n", " ", StringComparison.Ordinal).Trim()[i..(i + 30)]))
            .ToArray();
        Verify(many).Verified.Should().HaveCount(CitationVerifier.MaxCitations);
    }

    private static CitationCheck Verify(params CitedQuote[] citations) => CitationVerifier.Verify(citations, Passages);
}
