using System.Xml.Linq;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Parses Imprivata ProveID Web API AuthUser request XML into a DTO.
/// Returns null when the input is not a well-formed AuthUser request.
/// </summary>
public static class ImprivataXmlParser
{
    public static ImprivataAuthRequest? TryParseAuthUser(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }

        var root = doc.Root;
        if (root is null) return null;

        // The Imprivata schema uses no default namespace in the examples we've
        // seen. Match element names locally to be tolerant either way.
        var modalityInput = root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModalityAuthInput");
        var serverState = root.Elements().FirstOrDefault(e => e.Name.LocalName == "ServerState")?.Value;
        var createTicket = ParseBool(
            root.Elements().FirstOrDefault(e => e.Name.LocalName == "CreateAuthTicket")?.Value,
            defaultValue: true);

        if (modalityInput is null) return null;
        var modality = (string?)modalityInput.Attribute("modalityID");
        if (string.IsNullOrEmpty(modality)) return null;

        var authRequest = modalityInput.Elements().FirstOrDefault(e => e.Name.LocalName == "AuthRequest");

        string? username = null, domain = null, password = null, uniqueId = null, pin = null;

        if (authRequest is not null)
        {
            switch (modality)
            {
                case "PWD":
                    var pvr = authRequest.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "PasswordVerificationRequest");
                    var identity = pvr?.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "UserIdentity");
                    username = identity?.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "Username")?.Value;
                    domain = identity?.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "Domain")?.Value;
                    password = pvr?.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "Password")?.Value;
                    break;

                case "UID":
                    uniqueId = authRequest.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "UniqueID")?.Value;
                    break;

                case "PIN":
                    pin = authRequest.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "PIN")?.Value;
                    break;
            }
        }

        return new ImprivataAuthRequest(
            ModalityId: modality,
            Username: username,
            Domain: domain,
            Password: password,
            UniqueId: uniqueId,
            Pin: pin,
            ServerState: serverState,
            CreateAuthTicket: createTicket);
    }

    private static bool ParseBool(string? v, bool defaultValue)
    {
        if (string.IsNullOrEmpty(v)) return defaultValue;
        if (bool.TryParse(v, out var b)) return b;
        if (v == "1") return true;
        if (v == "0") return false;
        return defaultValue;
    }
}
