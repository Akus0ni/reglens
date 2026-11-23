namespace RegLens.Domain.Corpus;

/// <summary>The extracted text of one PDF page.</summary>
public sealed record PageText(int Number, string Text);
