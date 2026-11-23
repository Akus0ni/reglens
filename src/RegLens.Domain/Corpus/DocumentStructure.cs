namespace RegLens.Domain.Corpus;

/// <summary>How a document is laid out, which decides how it is cut into passages.</summary>
public enum DocumentStructure
{
    /// <summary>RBI Directions: chapters, lettered sections, numbered paragraphs, annexes.</summary>
    RbiDirections,

    /// <summary>Gazette rules: numbered rules, then schedules split into parts.</summary>
    GazetteRules,

    /// <summary>A short corrigendum, kept whole.</summary>
    Corrigendum,
}
