namespace RegLens.Domain.Chunking;

/// <summary>
/// A rough token count: characters divided by four, rounded up (spec 0001, P3).
/// Only used to keep chunks well inside the embedding model's 2K-token context.
/// </summary>
public static class TokenEstimate
{
    public const int CharactersPerToken = 4;

    public static int Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (text.Length + CharactersPerToken - 1) / CharactersPerToken;
    }
}
