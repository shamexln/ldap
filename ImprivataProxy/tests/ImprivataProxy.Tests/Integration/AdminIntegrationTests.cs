using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Facades.Admin;
using ImprivataProxy.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImprivataProxy.Tests.Integration;

public class AdminIntegrationTests
{
    private static AuthenticationHeaderValue BasicAdminHeader()
    {
        var b = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("admin:" + IntegrationAppFactory.AdminPassword));
        return new AuthenticationHeaderValue("Basic", b);
    }

    [Fact]
    public async Task WithoutCredentials_Returns401_WithBasicChallenge()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains(res.Headers.WwwAuthenticate, h =>
            h.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WithWrongPassword_Returns401()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong")));

        var res = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ListUsers_WithAdminAuth_Returns200()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP");
        await factory.SeedUserAsync("u2", "bob", "CORP");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = BasicAdminHeader();

        var res = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = await res.Content.ReadFromJsonAsync<List<UserListItemDto>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task IssueCard_ThenUid_LoginSucceeds()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = BasicAdminHeader();

        var issue = await client.PostAsJsonAsync("/admin/cards",
            new { userId = "u1", cardUid = "card-xyz", label = "main" });
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);

        // Now attempt UID login (without PIN since we didn't set one).
        client.DefaultRequestHeaders.Authorization = null;
        var body = new StringContent(@"<Request>
  <ModalityAuthInput modalityID=""UID""><AuthRequest><UniqueID>card-xyz</UniqueID></AuthRequest></ModalityAuthInput>
  <CreateAuthTicket>true</CreateAuthTicket>
</Request>", Encoding.UTF8, "text/xml");
        var res = await client.PostAsync("/sso/ProveIDWeb/v28/AuthUser", body);

        var xml = await res.Content.ReadAsStringAsync();
        Assert.Contains("disp=\"0\"", xml);
    }

    [Fact]
    public async Task SetPin_ThenCheckDb_HashIsPresent()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = BasicAdminHeader();

        var res = await client.PutAsJsonAsync("/admin/users/u1/pin",
            new { pin = "1234" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync("u1");
        Assert.NotNull(user!.PinHash);
    }

    [Fact]
    public async Task Sync_TriggersFakeLdap_Returns200WithStats()
    {
        using var factory = new IntegrationAppFactory();
        factory.Ldap.Users.Add(new Sources.ActiveDirectory.AdUserDto(
            ObjectGuid: Guid.NewGuid(),
            Username: "alice",
            Domain: "CORP",
            DistinguishedName: "CN=alice,DC=corp",
            DisplayName: "Alice",
            GivenName: null,
            Sn: null,
            Mail: null,
            Groups: Array.Empty<string>(),
            Enabled: true));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = BasicAdminHeader();

        var res = await client.PostAsync("/admin/sync", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.AnyAsync(u => u.Username == "alice"));
    }

    [Fact]
    public async Task OStickTicket_CannotAccessAdminEndpoints()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP", pwdPlaintext: "p@ss");
        using var client = factory.CreateClient();

        // Log in with OStick and try to use that ticket against /admin.
        var pwdXml = new StringContent(@"<Request><ModalityAuthInput modalityID=""PWD""><AuthRequest>
<PasswordVerificationRequest><UserIdentity><Username>alice</Username><Domain>CORP</Domain></UserIdentity>
<Password>p@ss</Password></PasswordVerificationRequest></AuthRequest></ModalityAuthInput>
<CreateAuthTicket>true</CreateAuthTicket></Request>", Encoding.UTF8, "text/xml");

        var login = await client.PostAsync("/sso/ProveIDWeb/v28/AuthUser", pwdXml);
        var loginDoc = System.Xml.Linq.XDocument.Parse(await login.Content.ReadAsStringAsync());
        var ticket = (string?)loginDoc.Root!.Element("AuthTicket");

        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
        req.Headers.Add("Authorization", Facades.Imprivata.OStickHeader.Format(ticket!));
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
