using RegLens.Domain.Chunking;

namespace RegLens.Application.Ingestion;

/// <summary>A chunk with its embedding vector.</summary>
public sealed record EmbeddedChunk(Chunk Chunk, ReadOnlyMemory<float> Vector);
