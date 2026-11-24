using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RegLens.Application;
using RegLens.Application.Ingestion;
using RegLens.Infrastructure;
using RegLens.Infrastructure.Persistence;
using RegLens.Infrastructure.Telemetry;
using RegLens.Ingest;

IngestRequest request;
try
{
    request = IngestCommandLine.Parse(args);
}
catch (ArgumentException exception)
{
    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    return 2;
}

// Options are parsed above, so the host gets no args. appsettings.json sits next to the binary.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddUserSecrets(typeof(IngestCommandLine).Assembly, optional: true);
builder.Services.AddRegLensApplication().AddRegLensInfrastructure(builder.Configuration);
builder.Services.AddRegLensTelemetry(builder.Configuration, "reglens-ingest");

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RegLens.Ingest");

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

// Starting the host starts OpenTelemetry's providers; stopping it flushes the last spans (spec 0006, R6.14).
await host.StartAsync(cancellation.Token).ConfigureAwait(false);
try
{
    await host.Services.GetRequiredService<SqlMigrationRunner>().ApplyAsync(cancellation.Token).ConfigureAwait(false);
    await host.Services.GetRequiredService<IngestCorpus>().RunAsync(request, cancellation.Token).ConfigureAwait(false);
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    IngestLog.Cancelled(log);
    return 130;
}
catch (Exception exception)
{
    IngestLog.Failed(log, exception);
    return 1;
}
finally
{
    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
}
