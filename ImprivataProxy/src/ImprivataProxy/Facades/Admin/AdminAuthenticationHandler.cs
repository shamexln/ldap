using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ImprivataProxy.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Facades.Admin;

public class AdminAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Admin";

    private readonly AdminConfig _config;

    public AdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IOptions<AdminConfig> adminConfig)
        : base(options, loggerFactory, encoder)
    {
        _config = adminConfig.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) || authHeader.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parsed = BasicAuthParser.TryParse(authHeader.ToString());
        if (parsed is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var (user, password) = parsed.Value;
        var expectedPassword = Environment.GetEnvironmentVariable(_config.PasswordEnvVar);
        if (string.IsNullOrEmpty(expectedPassword))
        {
            Logger.LogError("Admin password env var '{Var}' not set; rejecting all admin requests",
                _config.PasswordEnvVar);
            return Task.FromResult(AuthenticateResult.Fail("admin password not configured"));
        }

        if (!FixedTimeEquals(user, _config.Username) ||
            !FixedTimeEquals(password, expectedPassword))
        {
            return Task.FromResult(AuthenticateResult.Fail("invalid admin credentials"));
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, user),
            new Claim(ClaimTypes.Role, "admin"),
        }, SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Emit a proper 401 with WWW-Authenticate so curl / browsers prompt.
        Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{_config.Realm}\", charset=\"UTF-8\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
