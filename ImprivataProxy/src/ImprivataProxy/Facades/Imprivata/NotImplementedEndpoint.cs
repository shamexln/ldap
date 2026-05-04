using System.Xml.Linq;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Handler for Imprivata protocol endpoints we intentionally do not implement
/// (Password / Enrollment / Multi / VdiAccess / ConfigObject / SAMLArtifact / UserAppCreds).
/// Returns HTTP 501 with an Imprivata-style failure XML body so clients get a
/// structured response rather than a naked 404/500.
/// </summary>
public static class NotImplementedEndpoint
{
    public static IResult HandleAsync(HttpContext context)
    {
        var resource = ExtractResource(context.Request.Path.Value ?? "");
        return Results.Content(BuildXml(resource),
            AuthUserEndpoint.XmlContentType, statusCode: 501);
    }

    public static string BuildXml(string resource)
    {
        var response = new XElement("Response",
            new XElement("AuthState",
                new XAttribute("disp", ReturnCodes.DispFailure),
                new XAttribute("rtc", ReturnCodes.RtcModalityNotSupported),
                new XElement("FailureReason",
                    $"resource '{resource}' not implemented by this proxy")));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }

    public static string ExtractResource(string pathValue)
    {
        // path looks like /sso/ProveIDWeb/v28/Password/...
        var idx = pathValue.IndexOf("/ProveIDWeb/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "unknown";
        var rest = pathValue[(idx + "/ProveIDWeb/".Length)..];
        // rest = "v28/Password/..." → skip the version segment
        var slash = rest.IndexOf('/');
        if (slash < 0) return "unknown";
        var afterVersion = rest[(slash + 1)..];
        var end = afterVersion.IndexOf('/');
        return end < 0 ? afterVersion : afterVersion[..end];
    }
}
