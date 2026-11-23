namespace RegLens.Domain.Chunking;

/// <summary>
/// A passage cut out by a <see cref="IChunkingStrategy"/> before size limits apply.
/// <paramref name="Lead"/> is text repeated at the top of a split part so it stands alone; it does not affect pages.
/// </summary>
public sealed record Passage(string ParaRef, string Heading, IReadOnlyList<PageLine> Lines, string Lead = "")
{
    public string Content => Lead.Length == 0
        ? string.Join('\n', Lines.Select(l => l.Text))
        : Lead + "\n" + string.Join('\n', Lines.Select(l => l.Text));

    public int PageStart => Lines.Min(l => l.Page);

    public int PageEnd => Lines.Max(l => l.Page);
}
