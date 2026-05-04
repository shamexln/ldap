using System.Xml.Linq;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// GET /sso/ProveIDWeb/v{version}/Modalities
/// Advertises the authentication methods this proxy supports.
/// Only PWD, UID, and PIN are supported by design; others are deliberately omitted.
/// </summary>
public static class ModalitiesEndpoint
{
    public static IResult GetAsync() =>
        Results.Content(BuildXml(), AuthUserEndpoint.XmlContentType, statusCode: 200);

    public static string BuildXml()
    {
        var response = new XElement("Response",
            new XElement("Modalities",
                new XElement("Modality",
                    new XAttribute("id", "PWD"),
                    new XAttribute("enabled", "true")),
                new XElement("Modality",
                    new XAttribute("id", "UID"),
                    new XAttribute("enabled", "true")),
                new XElement("Modality",
                    new XAttribute("id", "PIN"),
                    new XAttribute("enabled", "true"),
                    // PIN is only meaningful as a second factor after UID in this proxy.
                    new XAttribute("standalone", "false"))));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }
}
