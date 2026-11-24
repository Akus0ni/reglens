using System.Diagnostics;

namespace RegLens.Application.Telemetry;

/// <summary>RegLens's own spans (spec 0006, R6.14), under the source the hosts' tracer listens to.</summary>
public static class RegLensActivities
{
    public const string SourceName = "RegLens";

    public static ActivitySource Source { get; } = new(SourceName);
}
