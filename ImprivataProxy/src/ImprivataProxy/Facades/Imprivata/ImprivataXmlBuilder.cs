using System.Xml.Linq;
using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Builds Imprivata ProveID Web API AuthUser response XML.
/// Shape matches the Imprivata 7.2 SP1 developer reference and our samples in designdoc/LDAP.md.
/// </summary>
public static class ImprivataXmlBuilder
{
    public static string Success(string modalityId, User user, string authTicket, string? responseDomain = null)
    {
        var domainId = user.AdObjectGuid ?? user.Id;
        var displayDomain = responseDomain ?? user.Domain;
        var netbiosDomain = ExtractNetBiosDomain(displayDomain);

        var response = new XElement("Response");
        response.Add(new XElement("AuthState", new XAttribute("disp", ReturnCodes.DispSuccess)));
        response.Add(new XElement("ModalityAuthOutput",
            new XAttribute("disp", ReturnCodes.DispSuccess),
            new XAttribute("modalityID", modalityId)));
        response.Add(new XElement("Principal",
            new XAttribute("displayName", user.DisplayName ?? user.Username),
            new XAttribute("id", user.Id),
            new XElement("UserIdentity",
                new XAttribute("domainID", domainId),
                new XAttribute("id", user.Id),
                new XElement("Username", user.Username),
                new XElement("UserDirType", "AD"),
                new XElement("Domain", new XAttribute("meaning", "DNS"), displayDomain),
                new XElement("Domain", new XAttribute("meaning", "NetBIOS"), netbiosDomain))));

        foreach (var enrollment in BuildModalityEnrollments(user))
            response.Add(enrollment);

        response.Add(new XElement("AuthTicket", authTicket));
        response.Add(BuildUserPolicy(user));

        return ToXmlString(response);
    }

    public static string CredentialFailure(string modalityId, User user, string? responseDomain = null)
    {
        var displayDomain = responseDomain ?? user.Domain;
        var netbiosDomain = ExtractNetBiosDomain(displayDomain);
        var domainId = user.AdObjectGuid ?? user.Id;

        var response = new XElement("Response");
        response.Add(new XElement("AuthState",
            new XAttribute("disp", ReturnCodes.DispCredentialFailure),
            new XAttribute("error", "14"),
            new XAttribute("errorText", "Invalid credentials.")));
        response.Add(new XElement("ModalityAuthOutput",
            new XAttribute("disp", ReturnCodes.DispCredentialFailure),
            new XAttribute("error", "1326"),
            new XAttribute("errorText", "Logon failure: unknown user name or bad password."),
            new XAttribute("modalityID", modalityId)));
        response.Add(new XElement("Principal",
            new XAttribute("displayName", user.DisplayName ?? user.Username),
            new XAttribute("id", user.Id),
            new XElement("UserIdentity",
                new XAttribute("domainID", domainId),
                new XAttribute("id", user.Id),
                new XElement("Username", user.Username),
                new XElement("UserDirType", "AD"),
                new XElement("Domain", new XAttribute("meaning", "DNS"), displayDomain),
                new XElement("Domain", new XAttribute("meaning", "NetBIOS"), netbiosDomain))));

        foreach (var enrollment in BuildModalityEnrollments(user))
            response.Add(enrollment);

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

    private static List<XElement> BuildModalityEnrollments(User user)
    {
        var enrollments = new List<XElement>
        {
            new XElement("ModalityEnrollment",
                new XAttribute("allowed", "true"),
                new XAttribute("enrolled", "true"),
                new XAttribute("force", "false"),
                new XAttribute("modalityID", "PWD"),
                new XAttribute("prompt", "false")),
            new XElement("ModalityEnrollment",
                new XAttribute("allowed", "true"),
                new XAttribute("enrolled", user.PinHash != null ? "true" : "false"),
                new XAttribute("force", user.PinHash == null ? "true" : "false"),
                new XAttribute("modalityID", "PIN"),
                new XAttribute("prompt", user.PinHash == null ? "true" : "false"),
                new XElement("EnrollPolicy",
                    new XElement("MinLength", "4"),
                    new XElement("MaxLength", "4"),
                    new XElement("ExtendedAllowed", "false"),
                    new XElement("HistorySize", "0"),
                    new XElement("ForceNoRepeatingNumbers", "false"),
                    new XElement("ForceNoSequentialNumbers", "false"),
                    new XElement("AllowSSReset", "false"))),
            new XElement("ModalityEnrollment",
                new XAttribute("allowed", "true"),
                new XAttribute("enrolled", user.Cards.Count > 0 ? "true" : "false"),
                new XAttribute("force", "false"),
                new XAttribute("modalityID", "UID"),
                new XAttribute("prompt", "false"),
                new XElement("EnrollPolicy",
                    new XElement("MaxQty", "0"),
                    new XElement("AllowReplacement", "false")))
        };

        return enrollments;
    }

    private static XElement BuildUserPolicy(User user)
    {
        return new XElement("userPolicy",
            new XAttribute("showTeaser", "true"),
            new XElement("authentication",
                new XElement("fingerAttempts", "2"),
                new XElement("failureCount", "5"),
                new XElement("failureCountInterval", "5"),
                new XElement("lockoutInterval", "5"),
                new XElement("offlineSupport", "true"),
                new XElement("offlineLifeSpan", new XAttribute("limit", "true"), "7"),
                new XElement("offlineAMSupport", "true"),
                new XElement("allowedModalities",
                    new XElement("AuthCombination",
                        new XElement("modality",
                            new XAttribute("id", "3"),
                            new XAttribute("modalityID", "UID")),
                        new XElement("modality",
                            new XAttribute("id", "8"),
                            new XAttribute("modalityID", "PIN"))),
                    new XElement("AuthCombination",
                        new XElement("modality",
                            new XAttribute("id", "0"),
                            new XAttribute("modalityID", "PWD"))))));
    }

    private static string ExtractNetBiosDomain(string dnsDomain)
    {
        if (string.IsNullOrEmpty(dnsDomain)) return "";
        var parts = dnsDomain.Split('.');
        return parts[0].ToUpperInvariant();
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
