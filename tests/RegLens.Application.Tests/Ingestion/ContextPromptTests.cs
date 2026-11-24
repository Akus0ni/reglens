using RegLens.Application.Ingestion;
using RegLens.Domain.Chunking;

namespace RegLens.Application.Tests.Ingestion;

public class ContextPromptTests
{
    [Fact]
    public void WrapsTheDocumentLikeTheCookbook() // P1
    {
        ContextPrompt.DocumentBlock("Whole text.").Should().Be("<document>\nWhole text.\n</document>");
    }

    [Fact]
    public void UsesTheCookbookChunkPromptWithHeadingAndReference() // P1
    {
        var chunk = new Chunk("rbc", "Para 98", "Chapter III – Conduct › I. Responsibilities of DSA / DMA", 28, 28, "98. The NBFC shall ensure.");

        ContextPrompt.ChunkBlock(chunk).Should().Be("""
            Here is the chunk we want to situate within the whole document
            <chunk heading="Chapter III – Conduct › I. Responsibilities of DSA / DMA" ref="Para 98">
            98. The NBFC shall ensure.
            </chunk>

            Please give a short succinct context to situate this chunk within the overall document for the purposes of improving search retrieval of the chunk.
            Answer only with the succinct context and nothing else.
            """);
    }

    [Fact]
    public void EscapesAttributeValues()
    {
        var chunk = new Chunk("d", "Para \"1\"", "A < B & C", 1, 1, "1. Text.");

        ContextPrompt.ChunkBlock(chunk).Should().Contain("""<chunk heading="A &lt; B &amp; C" ref="Para &quot;1&quot;">""");
    }

    [Fact]
    public void JoinsTheIngestPagesInOrder() // P2
    {
        ContextPrompt.DocumentText([new(24, "First."), new(25, "Second.")]).Should().Be("First.\nSecond.");
    }
}
