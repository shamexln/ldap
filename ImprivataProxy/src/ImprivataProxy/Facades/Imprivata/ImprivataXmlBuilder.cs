using System.Xml.Linq;
using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Builds Imprivata ProveID Web API AuthUser response XML.
/// Shape matches the Imprivata 7.2 SP1 developer reference and our samples in designdoc/LDAP.md.
/// </summary>
public static class ImprivataXmlBuilder
{
    public static string Success(string modalityId, User user, string authTicket)
    {
        var response = new XElement("Response",
            new XElement("AuthState", new XAttribute("disp", ReturnCodes.DispSuccess)),
            new XElement("ModalityAuthOutput",
                new XAttribute("modalityID", modalityId),
                new XAttribute("disp", ReturnCodes.DispSuccess)),
            new XElement("Principal",
                new XAttribute("id", user.Id),
                new XAttribute("displayName", user.DisplayName ?? user.Username),
                new XElement("UserIdentity",
                    new XAttribute("id", user.Id),
                    new XElement("Username", user.Username),
                    new XElement("Domain", new XAttribute("meaning", "DNS"), user.Domain))),
            new XElement("AuthTicket", authTicket));

        return ToXmlString(response);
    }

    public static string Failure(string modalityId, int rtc, string? reason = null)
    {
        var authState = new XElement("AuthState",
            new XAttribute("disp", ReturnCodes.DispFailure),
            new XAttribute("rtc", rtc));
        if (!string.IsNullOrEmpty(reason))
        {
            authState.Add(new XElement("FailureReason", reason));
        }

        var response = new XElement("Response",
            authState,
            new XElement("ModalityAuthOutput",
                new XAttribute("modalityID", modalityId),
                new XAttribute("disp", ReturnCodes.DispFailure)));

        return ToXmlString(response);
    }

    public static string Pending(string completedModalityId, string serverState, string nextModalityId)
    {
        var response = new XElement("Response",
            new XElement("ServerState", serverState),
            new XElement("AuthState",
                new XAttribute("disp", ReturnCodes.DispPending),
                new XAttribute("rtc", 2)),
            new XElement("ModalityAuthOutput",
                new XAttribute("modalityID", completedModalityId),
                new XAttribute("disp", ReturnCodes.DispSuccess)),
            new XElement("RemainingAuthPolicy",
                new XElement("AuthPolicyOption",
                    new XElement("AuthPolicyItem",
                        new XAttribute("modalityID", nextModalityId)))));

        return ToXmlString(response);
    }

    private static string ToXmlString(XElement root)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            root);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }
}
