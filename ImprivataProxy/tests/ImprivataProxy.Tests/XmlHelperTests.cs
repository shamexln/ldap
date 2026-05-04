using ImprivataProxy.Shared.Xml;
using System.Xml.Linq;

namespace ImprivataProxy.Tests;

public class XmlHelperTests
{
    private const string SampleAuthRequest = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Request>
    <ModalityAuthInput modalityID=""PWD"">
        <AuthRequest>
            <PasswordVerificationRequest>
                <UserIdentity>
                    <Username>testuser</Username>
                    <Domain>testdomain.com</Domain>
                </UserIdentity>
                <Password>123456</Password>
            </PasswordVerificationRequest>
        </AuthRequest>
    </ModalityAuthInput>
    <CreateAuthTicket>true</CreateAuthTicket>
</Request>";

    private const string SampleAuthResponse = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Response>
    <AuthState disp=""0"" />
    <ModalityAuthOutput modalityID=""PWD"" disp=""0"" />
    <Principal id=""ABCDEF"" displayName=""Test User"">
        <UserIdentity id=""123"">
            <UserDirType>AD</UserDirType>
            <Username>testuser</Username>
            <Domain meaning=""DNS"">testdomain.com</Domain>
            <Domain meaning=""NetBIOS"">TESTDOMAIN</Domain>
        </UserIdentity>
    </Principal>
    <AuthTicket>abc123def456</AuthTicket>
</Response>";

    [Fact]
    public void TryParse_ValidXml_ReturnsDocument()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest);
        Assert.NotNull(doc);
    }

    [Fact]
    public void TryParse_InvalidXml_ReturnsNull()
    {
        var doc = XmlHelper.TryParse("not xml at all");
        Assert.Null(doc);
    }

    [Fact]
    public void TryParse_NullInput_ReturnsNull()
    {
        var doc = XmlHelper.TryParse(null);
        Assert.Null(doc);
    }

    [Fact]
    public void XPathExists_ExistingElement_ReturnsTrue()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest)!;
        Assert.True(XmlHelper.XPathExists(doc, "//ModalityAuthInput[@modalityID='PWD']"));
    }

    [Fact]
    public void XPathExists_NonExistingElement_ReturnsFalse()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest)!;
        Assert.False(XmlHelper.XPathExists(doc, "//ModalityAuthInput[@modalityID='UID']"));
    }

    [Fact]
    public void XPathGetValue_ExistingElement_ReturnsValue()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest)!;
        var username = XmlHelper.XPathGetValue(doc, "//UserIdentity/Username");
        Assert.Equal("testuser", username);
    }

    [Fact]
    public void XPathGetValue_NonExistingElement_ReturnsNull()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest)!;
        var value = XmlHelper.XPathGetValue(doc, "//NonExistent");
        Assert.Null(value);
    }

    [Fact]
    public void XPathSetValue_ModifiesElement()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest)!;
        XmlHelper.XPathSetValue(doc, "//UserIdentity/Username", "newuser");
        var result = XmlHelper.XPathGetValue(doc, "//UserIdentity/Username");
        Assert.Equal("newuser", result);
    }

    [Fact]
    public void XPathInsertElement_AddsChildElement()
    {
        var doc = XmlHelper.TryParse(SampleAuthRequest)!;
        XmlHelper.XPathInsertElement(doc, "//Request", "<CustomAttr name=\"test\">value</CustomAttr>");
        Assert.True(XmlHelper.XPathExists(doc, "//Request/CustomAttr[@name='test']"));
    }

    [Fact]
    public void XPathExists_AuthResponse_SuccessState()
    {
        var doc = XmlHelper.TryParse(SampleAuthResponse)!;
        Assert.True(XmlHelper.XPathExists(doc, "//AuthState[@disp='0']"));
    }

    [Fact]
    public void XPathGetValue_AuthResponse_Username()
    {
        var doc = XmlHelper.TryParse(SampleAuthResponse)!;
        var username = XmlHelper.XPathGetValue(doc, "//UserIdentity/Username");
        Assert.Equal("testuser", username);
    }
}
