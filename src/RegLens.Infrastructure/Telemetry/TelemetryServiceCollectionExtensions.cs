using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RegLens.Application.Telemetry;

namespace RegLens.Infrastructure.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    /// <summary>The GenAI spans of <c>UseOpenTelemetry</c> (M.E.AI <c>OpenTelemetryConsts.DefaultSourceName</c>).</summary>
    public const string ExtensionsAISource = "Experimental.Microsoft.Extensions.AI";

    /// <summary>The MCP C# SDK's ActivitySource and Meter (<c>Diagnostics.cs</c>).</summary>
    public const string ModelContextProtocolSource = "Experimental.ModelContextProtocol";

    /// <summary>
    /// Traces, metrics and logs for one host, exported over OTLP gRPC when <c>Telemetry:OtlpEndpoint</c> is set
    /// (spec 0006, R6.14). The host adds its own instrumentation (ASP.NET Core) to the returned builder.
    /// </summary>
    public static IOpenTelemetryBuilder AddRegLensTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new TelemetryOptions();
        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(RegLensActivities.SourceName, ExtensionsAISource, ModelContextProtocolSource)
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(ExtensionsAISource, ModelContextProtocolSource)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation())
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            builder.UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(options.OtlpEndpoint));
        }

        return builder;
    }
}
