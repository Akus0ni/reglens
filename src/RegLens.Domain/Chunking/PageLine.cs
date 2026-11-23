namespace RegLens.Domain.Chunking;

/// <summary>One cleaned line of text and the PDF page it came from.</summary>
public sealed record PageLine(int Page, string Text);
