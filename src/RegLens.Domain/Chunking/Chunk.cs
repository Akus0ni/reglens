namespace RegLens.Domain.Chunking;

/// <summary>A searchable passage with its regulatory address.</summary>
public sealed record Chunk(
    string DocumentId,
    string ParaRef,
    string Heading,
    int PageStart,
    int PageEnd,
    string Content)
{
    public int TokenEstimate => Chunking.TokenEstimate.Of(Content);

    public ContentHash Hash => ContentHash.Of(DocumentId, ParaRef, Content);
}
