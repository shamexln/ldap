using System.Xml.Linq;
using ImprivataProxy.Configuration;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// GET /sso/ProveIDWeb/v{version}/Domains
/// Returns domain list in Imprivata ProveID Web format with reverse domain mapping.
/// </summary>
public static class DomainsEndpoint
{
    public static async Task<IResult> GetAsync(
        IUserStore users,
        IOptions<ProxyConfig> proxyConfig,
        CancellationToken ct)
    {
        var config = proxyConfig.Value;
        var internalDomains = await users.GetDistinctEnabledDomainsAsync(ct);

        var reversedMapping = config.DomainMapping
            .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        var response = new XElement("Response");

        foreach (var intDomain in internalDomains)
        {
            var extDomain = reversedMapping.TryGetValue(intDomain, out var ext) ? ext : intDomain;
            var netbios = extDomain.Split('.')[0].ToUpperInvariant();
            var domainId = GenerateDeterministicGuid(extDomain);

            response.Add(new XElement("Domain",
                new XAttribute("id", domainId),
                new XElement("UserDirType", "AD"),
                new XElement("UseSSL", "false"),
                new XElement("Name", new XAttribute("meaning", "DNS"), extDomain),
                new XElement("Name", new XAttribute("meaning", "NetBIOS"), netbios),
                new XElement("SPN", $"host/ssohost4kerberos.{extDomain}@{netbios}.{extDomain.Split('.').Last().ToUpperInvariant()}")));
        }

        response.Add(new XElement("Domain",
            new XAttribute("id", GenerateDeterministicGuid("OneSignLocal")),
            new XElement("UserDirType", "OneSign"),
            new XElement("UseSSL", "false"),
            new XElement("Name", "OneSignLocal")));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return Results.Content(sw.ToString(), AuthUserEndpoint.XmlContentType, statusCode: 200);
    }

    private static string GenerateDeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes).ToString();
    }
}
