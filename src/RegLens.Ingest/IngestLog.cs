using Microsoft.Extensions.Logging;

namespace RegLens.Ingest;

internal static partial class IngestLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Cancelled. The document in progress was rolled back.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion failed")]
    public static partial void Failed(ILogger logger, Exception exception);
}
