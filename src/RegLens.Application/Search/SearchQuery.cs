namespace RegLens.Application.Search;

/// <summary>
/// What the store searches with. An empty <paramref name="Vector"/> means no dense side (the embedding was
/// unavailable, spec 0002 P5, or keyword-only mode); <paramref name="UseKeywords"/> false means no keyword side
/// (dense-only mode, spec 0004 P9).
/// </summary>
public sealed record SearchQuery(
    string Text,
    ReadOnlyMemory<float> Vector,
    int CandidatesPerSide,
    int Limit,
    KeywordScoring Scoring = KeywordScoring.TsRankCd,
    bool UseKeywords = true)
{
    public bool HasVector => !Vector.IsEmpty;
}
