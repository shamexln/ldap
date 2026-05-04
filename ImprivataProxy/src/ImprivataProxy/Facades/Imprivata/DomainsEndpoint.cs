using System.Xml.Linq;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// GET /sso/ProveIDWeb/v{version}/Domains
/// Returns the distinct set of domain names known to the proxy, derived from the
/// users table. Only enabled users contribute, so decommissioned domains fade out
/// of the list on their own after AD sync disables the last member.
///
/// ADR-0002 §8.1:不再直接依赖 AppDbContext,改走 IUserStore.
/// </summary>
public static class DomainsEndpoint
{
    public static async Task<IResult> GetAsync(IUserStore users, CancellationToken ct)
    {
        var domains = await users.GetDistinctEnabledDomainsAsync(ct);

        return Results.Content(BuildXml(domains),
            AuthUserEndpoint.XmlContentType, statusCode: 200);
    }

    public static string BuildXml(IEnumerable<string> domains)
    {
        var response = new XElement("Response",
            new XElement("Domains",
                domains.Select(d => new XElement("Domain",
                    new XAttribute("name", d),
                    new XAttribute("type", "AD")))));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }
}
