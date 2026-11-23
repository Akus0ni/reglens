using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;

namespace RegLens.Domain.Tests.Chunking;

public class DocumentChunkerTests
{
    private static readonly string Paragraphs = string.Join('\n',
        "Chapter I – Preliminary",
        "1. These Directions shall be called the Reserve Bank of India Directions.",
        "2. These Directions shall come into effect immediately.",
        "3. Repeal and saving: the existing Directions stand repealed.");

    [Fact]
    public void GivesTheSameChunksAndHashesForTheSameInput()
    {
        var first = DocumentChunker.CreateDefault().Chunk(Document("doc-a"), [new PageText(1, Paragraphs)]);
        var second = DocumentChunker.CreateDefault().Chunk(Document("doc-a"), [new PageText(1, Paragraphs)]);

        second.Select(c => c.Hash).Should().Equal(first.Select(c => c.Hash));
    }

    [Fact]
    public void GivesIdenticalTextInTwoDocumentsDifferentHashes()
    {
        var a = DocumentChunker.CreateDefault().Chunk(Document("doc-a"), [new PageText(1, Paragraphs)]);
        var b = DocumentChunker.CreateDefault().Chunk(Document("doc-b"), [new PageText(1, Paragraphs)]);

        a.Select(c => c.Hash).Should().NotIntersectWith(b.Select(c => c.Hash));
    }

    [Fact]
    public void NeverPutsTwoParagraphNumbersInOneChunk()
    {
        var chunks = DocumentChunker.CreateDefault().Chunk(Document("doc-a"), [new PageText(1, Paragraphs)]);

        chunks.Should().AllSatisfy(c =>
            c.Content.Split('\n').Count(line => StructureMarkers.UnitNumber(line) is not null).Should().BeLessThanOrEqualTo(1));
    }

    [Fact]
    public void KeepsACorrigendumWhole()
    {
        var chunks = DocumentChunker.CreateDefault().Chunk(
            Document("corr", DocumentStructure.Corrigendum),
            [new PageText(1, "CORRIGENDA\nG.S.R. 892(E).—In the notification ...\n(i) in page 24,–")]);

        chunks.Should().ContainSingle().Which.ParaRef.Should().Be(CorrigendumChunking.CorrigendumRef);
    }

    [Fact]
    public void RefusesAStructureItHasNoStrategyFor()
    {
        var chunker = new DocumentChunker(new PassageSplitter(), [new CorrigendumChunking()]);

        var act = () => chunker.Chunk(Document("doc-a"), [new PageText(1, Paragraphs)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RbiDirections*doc-a*");
    }

    private static SourceDocument Document(string id, DocumentStructure structure = DocumentStructure.RbiDirections) =>
        new(id, $"{id}.pdf", "Title", "Ref", "Version", "https://example.test", new string('0', 64), 1, 1, new PageRange(1, 1), structure);
}
