namespace ImprivataProxy.Sources.ActiveDirectory;

/// <summary>
/// Minimal RFC 4514 DN parser sufficient for AD sync use cases.
/// Not a full parser (does not handle all escapes, hex pairs, or multi-valued RDNs),
/// but correct for the common AD shapes we encounter:
///   CN=alice,OU=Users,DC=corp,DC=example,DC=com
///   CN=Domain Admins,CN=Users,DC=corp,DC=example,DC=com
/// </summary>
public static class DnParser
{
    /// <summary>
    /// Returns the value of the leftmost CN= component, or null if none.
    /// Example: "CN=Domain Admins,OU=...,DC=..." -> "Domain Admins"
    /// </summary>
    public static string? ExtractLeftmostCn(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return null;

        foreach (var rdn in SplitRdns(dn))
        {
            var eq = rdn.IndexOf('=');
            if (eq <= 0) continue;
            var type = rdn[..eq].Trim();
            if (type.Equals("CN", StringComparison.OrdinalIgnoreCase))
            {
                return Unescape(rdn[(eq + 1)..].Trim());
            }
        }
        return null;
    }

    /// <summary>
    /// Concatenates all DC= components into a dotted domain name.
    /// Example: "CN=alice,OU=Users,DC=corp,DC=example,DC=com" -> "corp.example.com"
    /// </summary>
    public static string? ExtractDomainFromDn(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return null;

        var parts = new List<string>();
        foreach (var rdn in SplitRdns(dn))
        {
            var eq = rdn.IndexOf('=');
            if (eq <= 0) continue;
            var type = rdn[..eq].Trim();
            if (type.Equals("DC", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(Unescape(rdn[(eq + 1)..].Trim()));
            }
        }
        return parts.Count == 0 ? null : string.Join('.', parts);
    }

    /// <summary>
    /// Splits a DN into its RDN components, respecting backslash escapes of commas.
    /// </summary>
    private static IEnumerable<string> SplitRdns(string dn)
    {
        var buf = new System.Text.StringBuilder();
        for (int i = 0; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                buf.Append(c);
                buf.Append(dn[++i]);
                continue;
            }
            if (c == ',')
            {
                if (buf.Length > 0) { yield return buf.ToString(); buf.Clear(); }
                continue;
            }
            buf.Append(c);
        }
        if (buf.Length > 0) yield return buf.ToString();
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var buf = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                buf.Append(s[++i]);
            }
            else
            {
                buf.Append(s[i]);
            }
        }
        return buf.ToString();
    }
}
