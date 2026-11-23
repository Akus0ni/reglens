using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>A corrigendum is short and only makes sense whole, so it is one passage (spec 0001, R1.14).</summary>
public sealed class CorrigendumChunking : IChunkingStrategy
{
    public const string CorrigendumRef = "Corrigendum";

    public DocumentStructure SupportedStructure => DocumentStructure.Corrigendum;

    public IReadOnlyList<Passage> Segment(IReadOnlyList<PageLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Count == 0 ? [] : [new Passage(CorrigendumRef, CorrigendumRef, lines)];
    }
}
