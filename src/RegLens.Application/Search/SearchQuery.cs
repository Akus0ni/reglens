namespace RegLens.Application.Search;

/// <summary>
/// What the store searches with. An empty <paramref name="Vector"/> means keyword-only search
/// (the embedding was unavailable, spec 0002 P5).
/// </summary>
public sealed record SearchQuery(string Text, ReadOnlyMemory<float> Vector, int CandidatesPerSide, int Limit)
{
    public bool HasVector => !Vector.IsEmpty;
}
