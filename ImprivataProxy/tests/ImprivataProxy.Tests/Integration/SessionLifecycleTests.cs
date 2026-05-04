using System.Net;
using System.Text;
using System.Xml.Linq;
using ImprivataProxy.Tests.Helpers;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Tokens;

namespace ImprivataProxy.Tests.Integration;

public class SessionLifecycleTests
{
    private const string AuthUserPath = "/sso/ProveIDWeb/v28/AuthUser";

    private static StringContent PwdRequest(string u, string d, string p) =>
        new($@"<Request><ModalityAuthInput modalityID=""PWD"">
<AuthRequest><PasswordVerificationRequest>
<UserIdentity><Username>{u}</Username><Domain>{d}</Domain></UserIdentity>
<Password>{p}</Password>
</PasswordVerificationRequest></AuthRequest>
</ModalityAuthInput><CreateAuthTicket>true</CreateAuthTicket></Request>",
            Encoding.UTF8, "text/xml");

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var res = await client.PostAsync(AuthUserPath, PwdRequest("alice", "CORP", "p@ss"));
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        return (string?)doc.Root!.Element("AuthTicket")
            ?? throw new InvalidOperationException("login didn't return a ticket");
    }

    [Fact]
    public async Task Whoami_WithoutTicket_Returns401()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.GetAsync(AuthUserPath);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Whoami_WithValidTicket_Returns200_AndUserInfo()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP",
            pwdPlaintext: "p@ss",
            displayName: "Alice Smith",
            groups: new[] { "Admins", "Users" });
        using var client = factory.CreateClient();

        var ticket = await LoginAsync(client);

        var req = new HttpRequestMessage(HttpMethod.Get, AuthUserPath);
        req.Headers.Add("Authorization", OStickHeader.Format(ticket));
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        var principal = doc.Root!.Element("Principal");
        Assert.NotNull(principal);
        Assert.Equal("alice",
            (string?)principal.Element("UserIdentity")!.Element("Username"));

        var groups = doc.Root.Element("Groups")?.Elements("Group").Select(g => (string?)g).ToList();
        Assert.NotNull(groups);
        Assert.Contains("Admins", groups);
    }

    [Fact]
    public async Task Cancel_RevokesTicket_SubsequentWhoamiReturns401()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP", pwdPlaintext: "p@ss");
        using var client = factory.CreateClient();

        var ticket = await LoginAsync(client);

        // CANCEL
        var cancelReq = new HttpRequestMessage(new HttpMethod("CANCEL"), AuthUserPath);
        cancelReq.Headers.Add("Authorization", OStickHeader.Format(ticket));
        var cancelRes = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Same ticket should now fail.
        var whoReq = new HttpRequestMessage(HttpMethod.Get, AuthUserPath);
        whoReq.Headers.Add("Authorization", OStickHeader.Format(ticket));
        var whoRes = await client.SendAsync(whoReq);
        Assert.Equal(HttpStatusCode.Unauthorized, whoRes.StatusCode);
    }

    [Fact]
    public async Task GarbageTicket_Returns401()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, AuthUserPath);
        req.Headers.Add("Authorization", OStickHeader.Format("not.a.real.jwt"));
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
