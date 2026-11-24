using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;
using static RegLens.Application.PromptText;

namespace RegLens.Application.Ingestion;

/// <summary>
/// Context prompt v1 (spec 0005, P1, P2): Anthropic's contextual-embeddings cookbook prompt, verbatim, with the chunk tag
/// carrying our heading path and reference. The document block is identical for every chunk of a document, which is
/// what lets the prompt cache hit. Text goes in raw as in the cookbook; only attribute values are escaped.
/// </summary>
public static class ContextPrompt
{
    /// <summary>Part of the context cache key and the pipeline; bump it whenever the prompt changes (P5, P6).</summary>
    public const string Version = "v1";

    /// <summary>The extracted text of the document's ingest pages, page by page: what the chunker reads (P2).</summary>
    public static string DocumentText(IReadOnlyList<PageText> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return string.Join('\n', pages.Select(page => page.Text));
    }

    /// <summary>First user content block; it carries the cache breakpoint.</summary>
    public static string DocumentBlock(string documentText) => $"<document>\n{documentText}\n</document>";

    /// <summary>Second user content block.</summary>
    public static string ChunkBlock(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return $"""
            Here is the chunk we want to situate within the whole document
            <chunk heading="{EscapeAttribute(chunk.Heading)}" ref="{EscapeAttribute(chunk.ParaRef)}">
            {chunk.Content}
            </chunk>

            Please give a short succinct context to situate this chunk within the overall document for the purposes of improving search retrieval of the chunk.
            Answer only with the succinct context and nothing else.
            """;
    }
}
