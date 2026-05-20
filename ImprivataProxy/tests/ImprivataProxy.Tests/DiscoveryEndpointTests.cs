using System.Xml.Linq;
using ImprivataProxy.Configuration;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Tests;

public class DiscoveryEndpointTests
{
    // ---- Servers -----------------------------------------------------------------------------

    [Fact]
    public void Servers_IncludesHostPortScheme()
    {
        var xml = ServersEndpoint.BuildXml("proxy.corp.local", 443, "https");
        var doc = XDocument.Parse(xml);

        var server = doc.Root!.Element("Servers")!.Element("Server");
        Assert.NotNull(server);
        Assert.Equal("proxy.corp.local", (string?)server.Attribute("address"));
        Assert.Equal("443", (string?)server.Attribute("port"));
        Assert.Equal("https", (string?)server.Attribute("scheme"));
        Assert.Equal("true", (string?)server.Attribute("primary"));
    }

    // ---- Domains -----------------------------------------------------------------------------

    [Fact]
    public async Task Domains_GetAsync_ReturnsDomainsFromStore()
    {
        using var ctx = new TestDbContext();
        ctx.Db.Users.AddRange(
            new User { Id = "1", Username = "a", Domain = "CORP", Enabled = true },
            new User { Id = "2", Username = "b", Domain = "DEV", Enabled = true });
        await ctx.Db.SaveChangesAsync();

        var proxyConfig = Options.Create(new ProxyConfig());
        var result = await DomainsEndpoint.GetAsync(new UserStore(ctx.Db), proxyConfig, default);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Domains_DbQuery_FiltersDisabledUsersAndDeduplicates()
    {
        using var ctx = new TestDbContext();
        ctx.Db.Users.AddRange(
            new User { Id = "1", Username = "a", Domain = "CORP", Enabled = true },
            new User { Id = "2", Username = "b", Domain = "CORP", Enabled = true },     // dup
            new User { Id = "3", Username = "c", Domain = "DEV", Enabled = true },
            new User { Id = "4", Username = "d", Domain = "GONE", Enabled = false });   // filtered
        await ctx.Db.SaveChangesAsync();

        var store = new UserStore(ctx.Db);
        var domains = await store.GetDistinctEnabledDomainsAsync(default);

        Assert.Equal(2, domains.Count);
        Assert.Contains("CORP", domains);
        Assert.Contains("DEV", domains);
        Assert.DoesNotContain("GONE", domains);
    }

    // ---- Modalities --------------------------------------------------------------------------

    [Fact]
    public void Modalities_AdvertisesExactlyPwdUidPin()
    {
        var xml = ModalitiesEndpoint.BuildXml();
        var doc = XDocument.Parse(xml);

        var ids = doc.Root!.Element("Modalities")!
            .Elements("Modality")
            .Select(e => (string?)e.Attribute("id"))
            .ToList();

        Assert.Equal(new[] { "PWD", "UID", "PIN" }, ids);
    }

    [Fact]
    public void Modalities_PinMarkedStandaloneFalse()
    {
        var xml = ModalitiesEndpoint.BuildXml();
        var doc = XDocument.Parse(xml);

        var pin = doc.Root!.Element("Modalities")!
            .Elements("Modality")
            .Single(e => (string?)e.Attribute("id") == "PIN");
        Assert.Equal("false", (string?)pin.Attribute("standalone"));
    }

    // ---- NotImplemented ----------------------------------------------------------------------

    [Theory]
    [InlineData("/sso/ProveIDWeb/v28/Password", "Password")]
    [InlineData("/sso/ProveIDWeb/v28/Password/", "Password")]
    [InlineData("/sso/ProveIDWeb/v28/Password/subthing", "Password")]
    [InlineData("/sso/ProveIDWeb/v1/Multi", "Multi")]
    [InlineData("/sso/ProveIDWeb/v28/VdiAccess/whatever", "VdiAccess")]
    public void NotImplemented_ExtractResource(string path, string expected)
    {
        Assert.Equal(expected, NotImplementedEndpoint.ExtractResource(path));
    }

    [Theory]
    [InlineData("/unrelated/path")]
    [InlineData("")]
    [InlineData("/sso/ProveIDWeb/v28")]   // missing resource after version
    public void NotImplemented_ExtractResource_UnparseableReturnsUnknown(string path)
    {
        Assert.Equal("unknown", NotImplementedEndpoint.ExtractResource(path));
    }

    [Fact]
    public void NotImplemented_BuildXml_HasDispFailAndRtc()
    {
        var xml = NotImplementedEndpoint.BuildXml("Password");
        var doc = XDocument.Parse(xml);

        var authState = doc.Root!.Element("AuthState")!;
        Assert.Equal("4", (string?)authState.Attribute("disp"));
        Assert.Equal("1030", (string?)authState.Attribute("rtc"));
        Assert.Contains("Password", (string?)authState.Element("FailureReason"));
    }
}
