using System.Xml.Linq;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Facades.Imprivata;

namespace ImprivataProxy.Tests;

public class ImprivataXmlBuilderTests
{
    private static User MakeUser() => new()
    {
        Id = "u-1",
        Username = "alice",
        Domain = "CORP",
        DisplayName = "Alice Smith",
    };

    [Fact]
    public void Success_ContainsDispZero_AuthTicket_AndPrincipal()
    {
        var xml = ImprivataXmlBuilder.Success("PWD", MakeUser(), "tkt-abc");
        var doc = XDocument.Parse(xml);

        Assert.Equal("0", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("PWD", (string?)doc.Root.Element("ModalityAuthOutput")!.Attribute("modalityID"));
        Assert.Equal("0", (string?)doc.Root.Element("ModalityAuthOutput")!.Attribute("disp"));
        Assert.Equal("tkt-abc", (string?)doc.Root.Element("AuthTicket"));

        var principal = doc.Root.Element("Principal");
        Assert.NotNull(principal);
        Assert.Equal("u-1", (string?)principal.Attribute("id"));
        Assert.Equal("Alice Smith", (string?)principal.Attribute("displayName"));
        Assert.Equal("alice", (string?)principal.Element("UserIdentity")!.Element("Username"));
        Assert.Equal("CORP", (string?)principal.Element("UserIdentity")!.Element("Domain"));
    }

    [Fact]
    public void Success_FallsBackToUsername_WhenDisplayNameMissing()
    {
        var user = MakeUser();
        user.DisplayName = null;
        var xml = ImprivataXmlBuilder.Success("PWD", user, "tkt");
        var doc = XDocument.Parse(xml);

        Assert.Equal("alice",
            (string?)doc.Root!.Element("Principal")!.Attribute("displayName"));
    }

    [Fact]
    public void Failure_SetsDispFourAndRtc()
    {
        var xml = ImprivataXmlBuilder.Failure("PWD", ReturnCodes.RtcInvalidCredentials, "bad creds");
        var doc = XDocument.Parse(xml);

        Assert.Equal("4", (string?)doc.Root!.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("1001", (string?)doc.Root!.Element("AuthState")!.Attribute("rtc"));
        Assert.Equal("bad creds", (string?)doc.Root.Element("AuthState")!.Element("FailureReason"));
        Assert.Equal("PWD", (string?)doc.Root.Element("ModalityAuthOutput")!.Attribute("modalityID"));
        Assert.Null(doc.Root.Element("AuthTicket"));
        Assert.Null(doc.Root.Element("Principal"));
    }

    [Fact]
    public void Failure_NullReason_OmitsFailureReasonElement()
    {
        var xml = ImprivataXmlBuilder.Failure("PWD", ReturnCodes.RtcAccountLocked);
        var doc = XDocument.Parse(xml);

        Assert.Null(doc.Root!.Element("AuthState")!.Element("FailureReason"));
    }

    [Fact]
    public void Pending_IncludesServerStateAndRemainingPolicy()
    {
        var xml = ImprivataXmlBuilder.Pending("UID", "srv-state-xyz", "PIN");
        var doc = XDocument.Parse(xml);

        Assert.Equal("srv-state-xyz", (string?)doc.Root!.Element("ServerState"));
        Assert.Equal("1", (string?)doc.Root.Element("AuthState")!.Attribute("disp"));
        Assert.Equal("UID", (string?)doc.Root.Element("ModalityAuthOutput")!.Attribute("modalityID"));
        Assert.Equal("0", (string?)doc.Root.Element("ModalityAuthOutput")!.Attribute("disp"));

        var item = doc.Root.Element("RemainingAuthPolicy")!
            .Element("AuthPolicyOption")!
            .Element("AuthPolicyItem");
        Assert.Equal("PIN", (string?)item!.Attribute("modalityID"));
    }
}
