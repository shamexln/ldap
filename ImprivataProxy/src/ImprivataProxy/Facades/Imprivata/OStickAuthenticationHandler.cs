using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ImprivataProxy.Configuration;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ImprivataProxy.Facades.Imprivata;

public class OStickAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "OStick";

    private readonly ISigningKeyProvider _keys;
    private readonly ITicketBlacklist _blacklist;
    private readonly TicketConfig _ticketConfig;

    public OStickAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        ISigningKeyProvider keys,
        ITicketBlacklist blacklist,
        IOptions<TicketConfig> ticketConfig)
        : base(options, loggerFactory, encoder)
    {
        _keys = keys;
        _blacklist = blacklist;
        _ticketConfig = ticketConfig.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth) || auth.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var ticket = OStickHeader.TryExtractTicket(auth.ToString());
        if (ticket is null)
        {
            return AuthenticateResult.NoResult();
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = _ticketConfig.Issuer,
            ValidateIssuer = true,
            ValidateAudience = false,
            IssuerSigningKey = _keys.ValidationKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        ClaimsPrincipal principal;
        SecurityToken validated;
        try
        {
            principal = handler.ValidateToken(ticket, parameters, out validated);
        }
        catch (SecurityTokenExpiredException)
        {
            return AuthenticateResult.Fail("ticket expired");
        }
        catch (SecurityTokenException ex)
        {
            return AuthenticateResult.Fail("ticket invalid: " + ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unexpected error validating OStick ticket");
            return AuthenticateResult.Fail("ticket validation error");
        }

        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (!string.IsNullOrEmpty(jti)
            && await _blacklist.IsBlacklistedAsync(jti, Context.RequestAborted))
        {
            return AuthenticateResult.Fail("ticket revoked");
        }

        var authTicket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(authTicket);
    }
}
