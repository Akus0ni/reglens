using System.Net;

namespace RegLens.Domain.Answers;

/// <summary>A citation as the model gave it: the passage id and a quote it claims is in that passage.</summary>
public sealed record CitedQuote(long ChunkId, string Quote);

/// <summary>Why a citation was dropped (spec 0003, R3.4).</summary>
public enum DropReason
{
    /// <summary>The chunk id was not one of the passages sent to the model.</summary>
    UnknownChunk,

    /// <summary>The quote is not in that passage's text.</summary>
    QuoteNotFound,

    /// <summary>The quote is shorter than <see cref="CitationVerifier.MinQuoteLength"/> after normalisation.</summary>
    QuoteTooShort,
}

public sealed record DroppedCitation(CitedQuote Citation, DropReason Reason);

public sealed record CitationCheck(IReadOnlyList<CitedQuote> Verified, IReadOnlyList<DroppedCitation> Dropped);

/// <summary>
/// The deterministic check around the model (spec 0003, R3.4, D13, D34): a citation survives only if it points at a
/// passage the model was given and its quote is really in that passage.
/// </summary>
public static class CitationVerifier
{
    /// <summary>Shorter quotes ("the NBFC shall") would match almost any chunk (P5).</summary>
    public const int MinQuoteLength = 20;

    public const int MaxCitations = 8;

    /// <param name="citations">What the model cited.</param>
    /// <param name="passages">The passages sent to the model: chunk id → full text.</param>
    public static CitationCheck Verify(IReadOnlyList<CitedQuote> citations, IReadOnlyDictionary<long, string> passages)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(passages);

        var verified = new List<CitedQuote>();
        var dropped = new List<DroppedCitation>();
        var seen = new HashSet<(long, string)>();
        foreach (var citation in citations)
        {
            // Passages are sent with < > & escaped (P4); the model may copy either form.
            var quote = QuoteText.Normalise(WebUtility.HtmlDecode(citation.Quote ?? ""));
            if (!passages.TryGetValue(citation.ChunkId, out var text))
            {
                dropped.Add(new DroppedCitation(citation, DropReason.UnknownChunk));
            }
            else if (quote.Length < MinQuoteLength)
            {
                dropped.Add(new DroppedCitation(citation, DropReason.QuoteTooShort));
            }
            else if (!QuoteText.Normalise(text).Contains(quote, StringComparison.Ordinal))
            {
                dropped.Add(new DroppedCitation(citation, DropReason.QuoteNotFound));
            }
            else if (seen.Add((citation.ChunkId, quote)) && verified.Count < MaxCitations)
            {
                verified.Add(citation with { Quote = quote });
            }
        }

        return new CitationCheck(verified, dropped);
    }
}
