using System.Xml.Linq;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// GET /sso/ProveIDWeb/v{version}/Servers
/// Imprivata clients call this at startup to discover the server list and failover
/// configuration. Since this proxy IS the server, we return a single entry pointing
/// back at whatever Host the client connected via.
/// </summary>
public static class ServersEndpoint
{
    public static IResult GetAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        var port = context.Request.Host.Port
                   ?? (context.Request.Scheme == "https" ? 443 : 80);
        var scheme = context.Request.Scheme;

        return Results.Content(BuildXml(host, port, scheme),
            AuthUserEndpoint.XmlContentType, statusCode: 200);
    }

    public static string BuildXml(string host, int port, string scheme)
    {
        var response = new XElement("Response",
            new XElement("Servers",
                new XElement("Server",
                    new XAttribute("address", host),
                    new XAttribute("port", port),
                    new XAttribute("scheme", scheme),
                    new XAttribute("primary", "true"))));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }
}
