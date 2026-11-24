using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace RegLens.Api.Tests;

/// <summary>The API with a test key, no telemetry export and the caller's substitutes (spec 0006, R6.19).</summary>
internal static class TestApi
{
    public const string Key = "test-key-0123456789abcdef0123456789abcdef";

    public static WebApplicationFactory<Program> Create(Action<IServiceCollection> replace, Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:ApiKey", Key);
            builder.UseSetting("Telemetry:OtlpEndpoint", "");
            configure?.Invoke(builder);
            builder.ConfigureTestServices(replace);
        });

    /// <summary>A client that sends the key, as every caller must.</summary>
    public static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, Key);
        return client;
    }
}
