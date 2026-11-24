namespace RegLens.Application;

/// <summary>Escaping shared by the prompts that wrap regulation text in tags (spec 0003, P4; spec 0005, P1).</summary>
internal static class PromptText
{
    // Only what could end a tag. WebUtility.HtmlEncode would also rewrite § and other Latin-1 characters as
    // &#167;, which the model would then copy into its output.
    public static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    public static string EscapeAttribute(string text) => Escape(text).Replace("\"", "&quot;", StringComparison.Ordinal);
}
