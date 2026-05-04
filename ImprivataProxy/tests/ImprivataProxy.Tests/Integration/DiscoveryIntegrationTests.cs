using System.Net;
using System.Xml.Linq;
using ImprivataProxy.Tests.Helpers;

namespace ImprivataProxy.Tests.Integration;

public class DiscoveryIntegrationTests
{
    [Fact]
    public async Task Servers_UnauthenticatedGet_Returns200_WithHostEntry()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/sso/ProveIDWeb/v28/Servers");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var xml = await res.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root!.Element("Servers")!.Element("Server"));
    }

    [Fact]
    public async Task Modalities_ReturnsPwdUidPin()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/sso/ProveIDWeb/v28/Modalities");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        var ids = doc.Root!.Element("Modalities")!
            .Elements("Modality").Select(e => (string?)e.Attribute("id")).ToList();
        Assert.Equal(new[] { "PWD", "UID", "PIN" }, ids);
    }

    [Fact]
    public async Task Domains_AggregatesDistinctFromDb()
    {
        using var factory = new IntegrationAppFactory();
        await factory.SeedUserAsync("1", "alice", "CORP");
        await factory.SeedUserAsync("2", "bob", "CORP");   // duplicate domain
        await factory.SeedUserAsync("3", "carol", "DEV");
        await factory.SeedUserAsync("4", "dave", "GONE", enabled: false);

        using var client = factory.CreateClient();
        var res = await client.GetAsync("/sso/ProveIDWeb/v28/Domains");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = XDocument.Parse(await res.Content.ReadAsStringAsync());
        var domains = doc.Root!.Element("Domains")!
            .Elements("Domain").Select(e => (string?)e.Attribute("name")).ToList();
        Assert.Contains("CORP", domains);
        Assert.Contains("DEV", domains);
        Assert.DoesNotContain("GONE", domains);   // filtered by enabled
        Assert.Equal(2, domains.Count);           // CORP deduplicated
    }

    [Theory]
    [InlineData("GET", "/sso/ProveIDWeb/v28/Password")]
    [InlineData("POST", "/sso/ProveIDWeb/v28/Password")]
    [InlineData("DELETE", "/sso/ProveIDWeb/v28/Enrollment")]
    [InlineData("POST", "/sso/ProveIDWeb/v28/Multi")]
    [InlineData("GET", "/sso/ProveIDWeb/v28/VdiAccess/something")]
    [InlineData("GET", "/sso/ProveIDWeb/v28/UserAppCreds")]
    public async Task UnimplementedResources_Return501_WithXmlBody(string method, string path)
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(new HttpMethod(method), path);
        var res = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)501, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(body);
        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var factory = new IntegrationAppFactory();
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
