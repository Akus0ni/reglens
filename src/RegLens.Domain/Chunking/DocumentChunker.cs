using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>
/// Turns a document's pages into chunks: clean, segment by the document's structure, then enforce the size limit.
/// Deterministic: the same pages always give the same chunks and hashes (spec 0001, R1.17).
/// </summary>
public sealed class DocumentChunker
{
    /// <summary>
    /// Changes whenever any strategy cuts the same pages differently, so stored documents are re-ingested (spec 0005, P6).
    /// 2 = the heading and paragraph fixes H1–H5 of spec 0005.
    /// </summary>
    public const string Version = "chunker 2";

    private readonly PassageSplitter _splitter;
    private readonly Dictionary<DocumentStructure, IChunkingStrategy> _strategies;

    public DocumentChunker(PassageSplitter splitter, IEnumerable<IChunkingStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(splitter);
        ArgumentNullException.ThrowIfNull(strategies);

        _splitter = splitter;
        _strategies = strategies.ToDictionary(s => s.SupportedStructure);
    }

    /// <summary>The chunker with every structure this corpus uses.</summary>
    public static DocumentChunker CreateDefault() => new(
        new PassageSplitter(),
        [
            new NumberedStructureChunking(StructureMarkers.RbiDirections),
            new NumberedStructureChunking(StructureMarkers.GazetteRules),
            new CorrigendumChunking(),
        ]);

    public IReadOnlyList<Chunk> Chunk(SourceDocument document, IReadOnlyList<PageText> pages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);

        if (!_strategies.TryGetValue(document.Structure, out var strategy))
        {
            throw new InvalidOperationException(
                $"No chunking strategy for structure '{document.Structure}' (document '{document.Id}').");
        }

        return strategy.Segment(TextCleaner.Clean(pages))
            .SelectMany(_splitter.Split)
            .Select(passage => new Chunk(
                document.Id, passage.ParaRef, passage.Heading, passage.PageStart, passage.PageEnd, passage.Content))
            .ToList();
    }
}
