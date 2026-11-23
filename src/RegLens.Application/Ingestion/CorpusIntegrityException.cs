namespace RegLens.Application.Ingestion;

/// <summary>A corpus file or manifest entry does not match what was recorded; nothing has been written.</summary>
public sealed class CorpusIntegrityException : Exception
{
    public CorpusIntegrityException()
    {
    }

    public CorpusIntegrityException(string message)
        : base(message)
    {
    }

    public CorpusIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
