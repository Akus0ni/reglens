using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>Cuts a document's cleaned lines into passages along its own structure.</summary>
public interface IChunkingStrategy
{
    DocumentStructure SupportedStructure { get; }

    IReadOnlyList<Passage> Segment(IReadOnlyList<PageLine> lines);
}
