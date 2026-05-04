using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Tests;

public class DnParserTests
{
    [Theory]
    [InlineData("CN=alice,OU=Users,DC=corp,DC=example,DC=com", "alice")]
    [InlineData("CN=Domain Admins,CN=Users,DC=corp,DC=com", "Domain Admins")]
    [InlineData("cn=bob,DC=foo,DC=bar", "bob")]
    public void ExtractLeftmostCn_Common(string dn, string expected)
    {
        Assert.Equal(expected, DnParser.ExtractLeftmostCn(dn));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("OU=Users,DC=corp,DC=com")]
    public void ExtractLeftmostCn_NoneReturnsNull(string? dn)
    {
        Assert.Null(DnParser.ExtractLeftmostCn(dn));
    }

    [Fact]
    public void ExtractLeftmostCn_EscapedComma()
    {
        // RFC 4514 escaped comma in value: CN=Smith\, John,OU=...
        Assert.Equal("Smith, John",
            DnParser.ExtractLeftmostCn(@"CN=Smith\, John,OU=Users,DC=corp,DC=com"));
    }

    [Theory]
    [InlineData("CN=alice,OU=Users,DC=corp,DC=example,DC=com", "corp.example.com")]
    [InlineData("CN=alice,DC=foo,DC=bar", "foo.bar")]
    [InlineData("CN=alice,dc=MixedCase,DC=com", "MixedCase.com")]
    public void ExtractDomainFromDn_Common(string dn, string expected)
    {
        Assert.Equal(expected, DnParser.ExtractDomainFromDn(dn));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("CN=alice,OU=Users")]
    public void ExtractDomainFromDn_NoDcComponents_ReturnsNull(string? dn)
    {
        Assert.Null(DnParser.ExtractDomainFromDn(dn));
    }
}
