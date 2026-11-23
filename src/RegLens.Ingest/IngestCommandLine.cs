using RegLens.Application.Ingestion;

namespace RegLens.Ingest;

/// <summary>Command-line options (spec 0001, R1.23): <c>--force</c> and <c>--document &lt;id&gt;</c>.</summary>
public static class IngestCommandLine
{
    public const string Usage = "Usage: dotnet run --project src/RegLens.Ingest [-- --force] [--document <id>]";

    public static IngestRequest Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var force = false;
        string? documentId = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--force":
                    force = true;
                    break;
                case "--document" when i + 1 < args.Count:
                    documentId = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[i]}'. {Usage}", nameof(args));
            }
        }

        return new IngestRequest(force, documentId);
    }
}
