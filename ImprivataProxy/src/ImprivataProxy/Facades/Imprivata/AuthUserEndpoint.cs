using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Xml.Linq;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.IdpCore.Tokens;

namespace ImprivataProxy.Facades.Imprivata;

public static class AuthUserEndpoint
{
    public const string XmlContentType = "text/xml; charset=utf-8";

    // ----- POST: main auth entrypoint ----------------------------------------------------------

    public static async Task<IResult> PostAsync(
        HttpContext context,
        IPwdAuthenticator pwd,
        IUidAuthenticator uid,
        IPinAuthenticator pin,
        ILogger<AuthUserEndpointMarker> logger,
        CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(context.Request.Body))
        {
            body = await reader.ReadToEndAsync(ct);
        }

        var req = ImprivataXmlParser.TryParseAuthUser(body);
        if (req is null)
        {
            logger.LogWarning("Malformed AuthUser request body");
            return Results.Content(
                ImprivataXmlBuilder.Failure("UNKNOWN", ReturnCodes.RtcInvalidRequest,
                    "malformed request"),
                XmlContentType,
                statusCode: 400);
        }

        return req.ModalityId switch
        {
            "PWD" => await HandlePwdAsync(req, pwd, ct),
            "UID" => await HandleUidAsync(req, uid, ct),
            "PIN" => await HandlePinAsync(req, pin, ct),
            _ => XmlResult(ImprivataXmlBuilder.Failure(
                req.ModalityId, ReturnCodes.RtcModalityNotSupported,
                $"unknown modality '{req.ModalityId}'"))
        };
    }

    // ----- GET: whoami --------------------------------------------------------------------------

    public static IResult GetAsync(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return XmlResult(ImprivataXmlBuilder.Failure(
                "UNKNOWN", ReturnCodes.RtcInvalidCredentials, "not authenticated"));
        }

        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var usn = user.FindFirst(JwtTicketIssuer.ClaimUsn)?.Value;
        var dom = user.FindFirst(JwtTicketIssuer.ClaimDom)?.Value;
        var groups = user.FindAll(JwtTicketIssuer.ClaimGrp).Select(c => c.Value).ToList();

        var response = new XElement("Response",
            new XElement("AuthState", new XAttribute("disp", ReturnCodes.DispSuccess)),
            new XElement("Principal",
                new XAttribute("id", sub ?? ""),
                new XElement("UserIdentity",
                    new XAttribute("id", sub ?? ""),
                    new XElement("Username", usn ?? ""),
                    new XElement("Domain", new XAttribute("meaning", "DNS"), dom ?? ""))));

        if (groups.Count > 0)
        {
            var groupElement = new XElement("Groups");
            foreach (var g in groups) groupElement.Add(new XElement("Group", g));
            response.Add(groupElement);
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return Results.Content(sw.ToString(), XmlContentType, statusCode: 200);
    }

    // ----- CANCEL: revoke current ticket --------------------------------------------------------

    public static async Task<IResult> CancelAsync(
        HttpContext context,
        ITicketBlacklist blacklist,
        IAuditLogger audit,
        CancellationToken ct)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return XmlResult(ImprivataXmlBuilder.Failure(
                "UNKNOWN", ReturnCodes.RtcInvalidCredentials, "not authenticated"));
        }

        var jti = user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expUnix = user.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (string.IsNullOrEmpty(jti))
        {
            return XmlResult(ImprivataXmlBuilder.Failure(
                "UNKNOWN", ReturnCodes.RtcSystemError, "ticket missing jti"));
        }

        DateTime expiresAt = DateTime.UtcNow.AddHours(24);  // conservative fallback
        if (long.TryParse(expUnix, out var expSec))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSec).UtcDateTime;
        }

        await blacklist.AddAsync(jti, expiresAt, ct);

        var usn = user.FindFirst(JwtTicketIssuer.ClaimUsn)?.Value;
        var dom = user.FindFirst(JwtTicketIssuer.ClaimDom)?.Value;
        await audit.LogAsync("ticket_revoked", usn, dom, detail: new { jti }, ct: ct);

        var response = new XElement("Response",
            new XElement("AuthState", new XAttribute("disp", ReturnCodes.DispSuccess)));
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), response);
        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return Results.Content(sw.ToString(), XmlContentType, statusCode: 200);
    }

    // ----- Helpers ------------------------------------------------------------------------------

    private static async Task<IResult> HandlePwdAsync(
        ImprivataAuthRequest req, IPwdAuthenticator pwd, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Username) ||
            string.IsNullOrEmpty(req.Domain) ||
            string.IsNullOrEmpty(req.Password))
        {
            return XmlResult(ImprivataXmlBuilder.Failure(
                "PWD", ReturnCodes.RtcInvalidRequest, "missing PWD fields"));
        }

        var result = await pwd.AuthenticateAsync(req.Username, req.Domain, req.Password, ct);
        return ToXmlResult("PWD", result);
    }

    private static async Task<IResult> HandleUidAsync(
        ImprivataAuthRequest req, IUidAuthenticator uid, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.UniqueId))
        {
            return XmlResult(ImprivataXmlBuilder.Failure(
                "UID", ReturnCodes.RtcInvalidRequest, "missing UniqueID"));
        }

        var result = await uid.AuthenticateAsync(req.UniqueId, ct);
        return ToXmlResult("UID", result);
    }

    private static async Task<IResult> HandlePinAsync(
        ImprivataAuthRequest req, IPinAuthenticator pin, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.ServerState) || string.IsNullOrEmpty(req.Pin))
        {
            return XmlResult(ImprivataXmlBuilder.Failure(
                "PIN", ReturnCodes.RtcInvalidRequest, "missing ServerState or PIN"));
        }

        var result = await pin.AuthenticateAsync(req.ServerState, req.Pin, ct);
        return ToXmlResult("PIN", result);
    }

    private static IResult ToXmlResult(string modality, AuthResult result) => result switch
    {
        AuthResult.Success s => XmlResult(ImprivataXmlBuilder.Success(modality, s.User, s.Ticket)),
        AuthResult.Failure f => XmlResult(ImprivataXmlBuilder.Failure(modality, f.Rtc, f.Reason)),
        AuthResult.Pending p => XmlResult(ImprivataXmlBuilder.Pending(modality, p.ServerState, p.PendingModality)),
        _ => XmlResult(ImprivataXmlBuilder.Failure(modality, ReturnCodes.RtcSystemError, "unexpected"))
    };

    private static IResult XmlResult(string xml) =>
        Results.Content(xml, XmlContentType, statusCode: 200);

    public sealed class AuthUserEndpointMarker { }
}
