using System.IdentityModel.Tokens.Jwt;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Configuration;
using ImprivataProxy.Tests.Helpers;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ImprivataProxy.Tests;

public class JwtTicketIssuerTests
{
    private static User MakeUser() => new()
    {
        Id = "user-guid-123",
        Username = "alice",
        Domain = "CORP",
        DisplayName = "Alice Smith",
        AttributesJson = """{"mail":"alice@corp.com","groups":["Admins","Users"]}""",
    };

    private static TicketConfig Config(int ttlHours = 8) => new()
    {
        Issuer = "test-issuer",
        TtlHours = ttlHours,
        SigningKeyPath = "unused-in-tests",
    };

    private static (JwtTicketIssuer issuer, TestSigningKeyProvider keys) MakeIssuer(int ttlHours = 8)
    {
        var keys = new TestSigningKeyProvider();
        var issuer = new JwtTicketIssuer(keys, Options.Create(Config(ttlHours)));
        return (issuer, keys);
    }

    [Fact]
    public void Issue_ReturnsSignedJwt_ValidatesWithSameKey()
    {
        var (issuer, keys) = MakeIssuer();
        var token = issuer.Issue(MakeUser());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = "test-issuer",
            ValidateIssuer = true,
            ValidateAudience = false,
            IssuerSigningKey = keys.ValidationKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
        }, out _);

        Assert.Equal("user-guid-123", principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.Equal("alice", principal.FindFirst(JwtTicketIssuer.ClaimUsn)!.Value);
        Assert.Equal("CORP", principal.FindFirst(JwtTicketIssuer.ClaimDom)!.Value);
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Jti));

        keys.Dispose();
    }

    [Fact]
    public void Issue_ExtractsGroupsFromAttributesJson()
    {
        var (issuer, keys) = MakeIssuer();
        var token = issuer.Issue(MakeUser());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = "test-issuer",
            ValidateIssuer = true,
            ValidateAudience = false,
            IssuerSigningKey = keys.ValidationKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
        }, out _);

        var groups = principal.FindAll(JwtTicketIssuer.ClaimGrp).Select(c => c.Value).ToList();
        Assert.Contains("Admins", groups);
        Assert.Contains("Users", groups);

        keys.Dispose();
    }

    [Fact]
    public void Issue_NoAttributesJson_NoGroupsInToken()
    {
        var (issuer, keys) = MakeIssuer();
        var user = MakeUser();
        user.AttributesJson = null;
        var token = issuer.Issue(user);

        var principal = new JwtSecurityTokenHandler().ValidateToken(token,
            new TokenValidationParameters
            {
                ValidIssuer = "test-issuer",
                IssuerSigningKey = keys.ValidationKey,
                ValidateAudience = false,
            }, out _);

        Assert.Empty(principal.FindAll(JwtTicketIssuer.ClaimGrp));
        keys.Dispose();
    }

    [Fact]
    public void Issue_DifferentUsers_HaveDifferentJti()
    {
        var (issuer, keys) = MakeIssuer();
        var t1 = issuer.Issue(MakeUser());
        var t2 = issuer.Issue(MakeUser());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var jti1 = handler.ReadJwtToken(t1).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = handler.ReadJwtToken(t2).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        Assert.NotEqual(jti1, jti2);
        keys.Dispose();
    }

    [Fact]
    public void Issue_ExpirySetToConfiguredTtl()
    {
        var (issuer, keys) = MakeIssuer(ttlHours: 4);
        var before = DateTime.UtcNow;
        var token = issuer.Issue(MakeUser());
        var after = DateTime.UtcNow;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var exp = jwt.ValidTo;
        // Expiry should be between before+4h and after+4h.
        Assert.True(exp >= before.AddHours(4).AddSeconds(-2));
        Assert.True(exp <= after.AddHours(4).AddSeconds(2));
        keys.Dispose();
    }

    [Fact]
    public void Token_FromOneKey_IsRejectedByAnotherKey()
    {
        var (issuer, keys1) = MakeIssuer();
        using var keys2 = new TestSigningKeyProvider();
        var token = issuer.Issue(MakeUser());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        // A different RSA key than the one that signed → any SecurityTokenException.
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = "test-issuer",
                IssuerSigningKey = keys2.ValidationKey,
                ValidateAudience = false,
            }, out _));

        keys1.Dispose();
    }
}
