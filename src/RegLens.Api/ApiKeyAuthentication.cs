using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RegLens.Api;

/// <summary>The API key (spec 0006, D58, R6.9). Section "Auth"; set by env var or user-secrets, never appsettings.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    private const string Where =
        "Set it with the env var Auth__ApiKey or `dotnet user-secrets set Auth:ApiKey <key> --project src/RegLens.Api`, never in appsettings.json.";

    [Required(ErrorMessage = "Auth:ApiKey is not set. " + Where)]
    [MinLength(32, ErrorMessage = "Auth:ApiKey must be at least 32 characters. " + Where)]
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// Authenticates a request by its <c>X-API-Key</c> header (spec 0006, D58, R6.8, P3). No header: no result, so a
/// protected endpoint answers 401; a wrong key: failure, also 401. The key is never logged.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    /// <summary>The caller's name: one key, one caller. It also keys the rate limits (P4).</summary>
    public const string CallerName = "api-key";

    private readonly byte[] _key = Encoding.UTF8.GetBytes(auth.Value.ApiKey);

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // FixedTimeEquals returns at once when the lengths differ; the key's length is not the secret.
        if (values.Count != 1 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(values[0] ?? ""), _key))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, CallerName)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
