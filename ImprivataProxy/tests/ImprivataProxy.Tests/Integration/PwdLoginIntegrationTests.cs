using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImprivataProxy.Tests.Integration;

public class PwdLoginIntegrationTests
{
    private const string AuthUserPath = "/sso/ProveIDWeb/v28/AuthUser";

    private static StringContent PwdRequest(string user, string domain, string password)
    {
        var body = $@"<Request>
  <ModalityAuthInput modalityID=""PWD"">
    <AuthRequest>
      <PasswordVerificationRequest>
        <UserIdentity>
          <Username>{user}</Username>
          <Domain>{domain}</Domain>
        </UserIdentity>
        <Password>{password}</Password>
      </PasswordVerificationRequest>
    </AuthRequest>
  </ModalityAuthInput>
  <CreateAuthTicket>true</CreateAuthTicket>
</Request>";
        return new StringContent(body, Encoding.UTF8, "text/xml");
    }

    [Fact]
    public async Task Login_WithLocalHash_Succeeds_AndIssuesTicket()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP", pwdPlaintext: "p@ss");

        using var client = factory.CreateClient();
        var res = await client.PostAsync(AuthUserPath, PwdRequest("alice", "CORP", "p@ss"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("0", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        var ticket = (string?)doc.Root.Element("AuthTicket");
        Assert.False(string.IsNullOrWhiteSpace(ticket));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsInvalidCredentialsFailure()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP", pwdPlaintext: "correct");

        using var client = factory.CreateClient();
        var res = await client.PostAsync(AuthUserPath, PwdRequest("alice", "CORP", "wrong"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("1001", (string?)doc.Root!.Element("AuthState")!.Attribute("rtc"));
    }

    [Fact]
    public async Task Login_FirstTime_TriggersAdBindFallback_AndCachesHash()
    {
        using var factory = new IntegrationAppFactory();
        // No pwd set locally — simulates first login after AD sync.
        await factory.SeedUserAsync("u1", "alice", "CORP");
        factory.Ldap.VerifyResults[("CN=alice,OU=Users,DC=corp,DC=example,DC=com", "real-pwd")] =
            ImprivataProxy.Sources.Contracts.RemoteVerifyOutcome.Valid;

        using var client = factory.CreateClient();
        var res = await client.PostAsync(AuthUserPath, PwdRequest("alice", "CORP", "real-pwd"));

        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("0", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));

        // Hash should now be persisted.
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.NotNull(user.PwdHash);
        Assert.NotNull(user.PwdHashUpdatedAt);
    }

    [Fact]
    public async Task Login_DisabledUser_RejectedAsInvalidCredentials()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP",
            pwdPlaintext: "p@ss", enabled: false);

        using var client = factory.CreateClient();
        var res = await client.PostAsync(AuthUserPath, PwdRequest("alice", "CORP", "p@ss"));

        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("1001", (string?)doc.Root!.Element("AuthState")!.Attribute("rtc"));
    }

    [Fact]
    public async Task Malformed_Body_Returns400_WithXml()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.PostAsync(AuthUserPath,
            new StringContent("<not>an auth request</not>", Encoding.UTF8, "text/xml"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.StartsWith("text/xml", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_AuditLog_RecordsSuccessWithUserAndDomain()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP", pwdPlaintext: "p@ss");

        using var client = factory.CreateClient();
        await client.PostAsync(AuthUserPath, PwdRequest("alice", "CORP", "p@ss"));

        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLog.Where(a => a.Event == "pwd_login_ok").ToListAsync();
        Assert.Single(audit);
        Assert.Equal("alice", audit[0].Username);
        Assert.Equal("CORP", audit[0].Domain);
    }

    [Fact]
    public async Task Login_XForwardedForHeader_RecordedAsClientIp()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP", pwdPlaintext: "p@ss");

        using var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, AuthUserPath)
        {
            Content = PwdRequest("alice", "CORP", "p@ss"),
        };
        req.Headers.Add("X-Forwarded-For", "10.1.2.3, 192.168.1.1");
        await client.SendAsync(req);

        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLog.Where(a => a.Event == "pwd_login_ok").ToListAsync();
        Assert.Single(audit);
        Assert.Equal("10.1.2.3", audit[0].ClientIp);
    }
}
