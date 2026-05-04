using System.Net;
using System.Text;
using System.Xml.Linq;
using ImprivataProxy.Tests.Helpers;

namespace ImprivataProxy.Tests.Integration;

public class UidPinIntegrationTests
{
    private const string AuthUserPath = "/sso/ProveIDWeb/v28/AuthUser";

    private static StringContent UidRequest(string uid) =>
        new($@"<Request>
  <ModalityAuthInput modalityID=""UID"">
    <AuthRequest><UniqueID>{uid}</UniqueID></AuthRequest>
  </ModalityAuthInput>
  <CreateAuthTicket>true</CreateAuthTicket>
</Request>", Encoding.UTF8, "text/xml");

    private static StringContent PinRequest(string serverState, string pin) =>
        new($@"<Request>
  <ServerState>{serverState}</ServerState>
  <ModalityAuthInput modalityID=""PIN"">
    <AuthRequest><PIN>{pin}</PIN></AuthRequest>
  </ModalityAuthInput>
</Request>", Encoding.UTF8, "text/xml");

    [Fact]
    public async Task CardWithoutPin_IssuesTicketImmediately()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP",
            cardUidPlaintext: "1234567890");   // no PIN

        using var client = factory.CreateClient();
        var res = await client.PostAsync(AuthUserPath, UidRequest("1234567890"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("0", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.False(string.IsNullOrWhiteSpace((string?)doc.Root!.Element("AuthTicket")));
    }

    [Fact]
    public async Task CardWithPin_TwoStepFlow_SucceedsWithCorrectPin()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP",
            cardUidPlaintext: "1234567890",
            pinPlaintext: "4242");

        using var client = factory.CreateClient();

        // Step 1: card scan → pending + ServerState
        var res1 = await client.PostAsync(AuthUserPath, UidRequest("1234567890"));
        var doc1 = XDocument.Parse(await res1.Content.ReadAsStringAsync());
        Assert.Equal("1", (string?)doc1.Root!.Element("AuthState")!.Attribute("disp"));
        var serverState = (string?)doc1.Root!.Element("ServerState");
        Assert.False(string.IsNullOrWhiteSpace(serverState));

        var nextItem = doc1.Root.Element("RemainingAuthPolicy")!
            .Element("AuthPolicyOption")!
            .Element("AuthPolicyItem");
        Assert.Equal("PIN", (string?)nextItem!.Attribute("modalityID"));

        // Step 2: PIN → ticket
        var res2 = await client.PostAsync(AuthUserPath, PinRequest(serverState!, "4242"));
        var doc2 = XDocument.Parse(await res2.Content.ReadAsStringAsync());
        Assert.Equal("0", (string?)doc2.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.False(string.IsNullOrWhiteSpace((string?)doc2.Root!.Element("AuthTicket")));
    }

    [Fact]
    public async Task WrongPin_ReturnsFailure_SessionRemainsForRetry()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("u1", "alice", "CORP",
            cardUidPlaintext: "card1", pinPlaintext: "1234");

        using var client = factory.CreateClient();
        var doc1 = XDocument.Parse(
            await (await client.PostAsync(AuthUserPath, UidRequest("card1"))).Content.ReadAsStringAsync());
        var state = (string?)doc1.Root!.Element("ServerState");

        // Wrong PIN
        var res = await client.PostAsync(AuthUserPath, PinRequest(state!, "9999"));
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("1001", (string?)doc.Root!.Element("AuthState")!.Attribute("rtc"));

        // Retry with correct PIN still works
        var retry = await client.PostAsync(AuthUserPath, PinRequest(state!, "1234"));
        var retryDoc = XDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal("0", (string?)retryDoc.Root!.Element("AuthState")!.Attribute("disp"));
    }

    [Fact]
    public async Task UnknownCard_ReturnsInvalidCredentials()
    {
        using var factory = new IntegrationAppFactory();

        using var client = factory.CreateClient();
        var res = await client.PostAsync(AuthUserPath, UidRequest("non-existent-card"));
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("1001", (string?)doc.Root!.Element("AuthState")!.Attribute("rtc"));
    }

    [Fact]
    public async Task UnknownServerState_ReturnsSessionExpired()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.PostAsync(AuthUserPath, PinRequest("bogus-state", "1234"));
        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("1020", (string?)doc.Root!.Element("AuthState")!.Attribute("rtc"));
    }
}
