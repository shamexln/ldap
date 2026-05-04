using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ImprivataProxy.IdpCore.Tokens;

public class JwtTicketIssuer : ITicketIssuer
{
    public const string ClaimUsn = "usn";
    public const string ClaimDom = "dom";
    public const string ClaimGrp = "grp";

    private readonly ISigningKeyProvider _keys;
    private readonly TicketConfig _config;

    public JwtTicketIssuer(ISigningKeyProvider keys, IOptions<TicketConfig> config)
    {
        _keys = keys;
        _config = config.Value;
    }

    public string Issue(User user)
    {
        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimUsn, user.Username),
            new(ClaimDom, user.Domain),
        };

        foreach (var group in ParseGroups(user.AttributesJson))
        {
            claims.Add(new Claim(ClaimGrp, group));
        }

        var token = new JwtSecurityToken(
            issuer: _config.Issuer,
            audience: null,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(_config.TtlHours),
            signingCredentials: _keys.SigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static IEnumerable<string> ParseGroups(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(attributesJson); }
        catch { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("groups", out var groups)) yield break;
            if (groups.ValueKind != JsonValueKind.Array) yield break;
            foreach (var g in groups.EnumerateArray())
            {
                if (g.ValueKind == JsonValueKind.String)
                {
                    var s = g.GetString();
                    if (!string.IsNullOrEmpty(s)) yield return s;
                }
            }
        }
    }
}
