using RegLens.Domain.Chunking;

namespace RegLens.Application.Ingestion;

/// <summary>A chunk and the context that situates it in its document; null when contextual retrieval is off (spec 0005).</summary>
public sealed record ContextualChunk(Chunk Chunk, string? Context);

/// <summary>A chunk with its context and embedding vector.</summary>
public sealed record EmbeddedChunk(Chunk Chunk, ReadOnlyMemory<float> Vector, string? Context = null);
