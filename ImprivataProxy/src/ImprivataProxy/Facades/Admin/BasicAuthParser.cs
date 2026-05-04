using System.Text;

namespace ImprivataProxy.Facades.Admin;

/// <summary>
/// Pure parser for HTTP Basic auth headers. Given "Basic &lt;base64(user:pass)&gt;",
/// returns (user, pass) or null on any shape error / decoding error.
/// </summary>
public static class BasicAuthParser
{
    public static (string user, string password)? TryParse(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return null;

        var trimmed = authorizationHeader.TrimStart();
        const string scheme = "Basic ";
        if (!trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var b64 = trimmed[scheme.Length..].Trim();
        if (string.IsNullOrEmpty(b64)) return null;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(b64); }
        catch (FormatException) { return null; }

        string decoded;
        try { decoded = Encoding.UTF8.GetString(bytes); }
        catch { return null; }

        var colon = decoded.IndexOf(':');
        if (colon < 0) return null;

        var user = decoded[..colon];
        var pass = decoded[(colon + 1)..];
        if (user.Length == 0) return null;
        return (user, pass);
    }
}
