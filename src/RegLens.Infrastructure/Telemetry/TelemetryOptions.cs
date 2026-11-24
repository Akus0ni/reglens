namespace RegLens.Infrastructure.Telemetry;

/// <summary>OpenTelemetry export (spec 0006, D62, R6.14–R6.15). Section "Telemetry".</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>OTLP gRPC endpoint (the Aspire dashboard). Empty turns export off.</summary>
    public string OtlpEndpoint { get; set; } = "";

    /// <summary>
    /// Record prompts and responses in GenAI spans (<c>EnableSensitiveData</c>). On by owner decision (D62); turn it
    /// off before RegLens runs anywhere but the owner's machine.
    /// </summary>
    public bool CaptureContent { get; set; } = true;
}
