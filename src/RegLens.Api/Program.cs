using RegLens.Api;
using RegLens.Application;
using RegLens.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
// A value that cannot bind (topK=abc) is the caller's mistake: 400, not a Development-only exception and 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddRegLensApplication().AddRegLensInfrastructure(builder.Configuration);
builder.Services.AddHostedService<EmbeddingWarmup>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapSearchEndpoints();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Visible to WebApplicationFactory in the API tests.</summary>
public partial class Program;
