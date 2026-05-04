using System.Xml.Linq;

namespace ImprivataProxy.Shared.Logging;

public class LogSanitizer
{
    private static readonly HashSet<string> SensitiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "OldPassword", "NewPassword",
        "UniqueID", "AuthTicket", "PIN",
        "UserIdentityPassword"
    };

    public string SanitizeXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return xml;

        try
        {
            var doc = XDocument.Parse(xml);
            SanitizeElement(doc.Root!);
            return doc.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return "[non-XML content]";
        }
    }

    private void SanitizeElement(XElement element)
    {
        if (SensitiveElements.Contains(element.Name.LocalName))
        {
            element.Value = "***";
            return;
        }

        foreach (var child in element.Elements())
        {
            SanitizeElement(child);
        }
    }

    public string SanitizeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var sanitized = headers.Select(h =>
        {
            var key = h.Key;
            var value = key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? "***"
                : string.Join(", ", h.Value);
            return $"{key}: {value}";
        });
        return string.Join("; ", sanitized);
    }
}
