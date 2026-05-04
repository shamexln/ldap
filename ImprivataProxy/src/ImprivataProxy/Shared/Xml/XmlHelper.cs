using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace ImprivataProxy.Shared.Xml;

public static class XmlHelper
{
    public static XDocument? TryParse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XDocument.Parse(xml);
        }
        catch
        {
            return null;
        }
    }

    public static bool XPathExists(XDocument doc, string xpath)
    {
        try
        {
            var result = doc.XPathSelectElement(xpath);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    public static string? XPathGetValue(XDocument doc, string xpath)
    {
        try
        {
            var element = doc.XPathSelectElement(xpath);
            return element?.Value;
        }
        catch
        {
            return null;
        }
    }

    public static XDocument XPathSetValue(XDocument doc, string xpath, string value)
    {
        var element = doc.XPathSelectElement(xpath);
        if (element != null)
        {
            element.Value = value;
        }
        return doc;
    }

    public static XDocument XPathInsertElement(XDocument doc, string parentXpath, string xmlFragment)
    {
        var parent = doc.XPathSelectElement(parentXpath);
        if (parent != null)
        {
            try
            {
                var newElement = XElement.Parse(xmlFragment);
                parent.Add(newElement);
            }
            catch
            {
                // Invalid XML fragment, skip
            }
        }
        return doc;
    }

    public static XDocument XPathReplaceElement(XDocument doc, string xpath, string xmlFragment)
    {
        var element = doc.XPathSelectElement(xpath);
        if (element != null)
        {
            try
            {
                var newElement = XElement.Parse(xmlFragment);
                element.ReplaceWith(newElement);
            }
            catch
            {
                // Invalid XML fragment, skip
            }
        }
        return doc;
    }
}
