namespace RegLens.Domain.Search;

/// <summary>How a citation is matched against a chunk's paragraph reference (spec 0002, R2.3).</summary>
public enum CitationMatch
{
    /// <summary>"Para 6" matches "Para 6" and "Para 6, part 2 of 3", never "Para 60".</summary>
    Prefix,

    /// <summary>"Item 4" also matches the end of a schedule reference, e.g. "First Schedule, Part A, Item 4".</summary>
    PrefixOrSuffix,
}

/// <summary>A regulatory reference named in a query, normalised to the chunk <c>para_ref</c> format.</summary>
public sealed record Citation(string Reference, CitationMatch Match)
{
    /// <summary>True when a chunk with this paragraph reference is what the citation names.</summary>
    public bool Matches(string paraRef)
    {
        ArgumentNullException.ThrowIfNull(paraRef);

        return paraRef.Equals(Reference, StringComparison.Ordinal)
            || paraRef.StartsWith(Reference + ",", StringComparison.Ordinal)
            || (Match == CitationMatch.PrefixOrSuffix && paraRef.EndsWith(", " + Reference, StringComparison.Ordinal));
    }
}
