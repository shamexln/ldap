using System.Text;
using ImprivataProxy.Facades.Admin;

namespace ImprivataProxy.Tests;

public class BasicAuthParserTests
{
    private static string Encode(string user, string pass) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));

    [Fact]
    public void Parse_Standard_ExtractsUserAndPass()
    {
        var header = Encode("admin", "sekret");
        var parsed = BasicAuthParser.TryParse(header);
        Assert.NotNull(parsed);
        Assert.Equal("admin", parsed.Value.user);
        Assert.Equal("sekret", parsed.Value.password);
    }

    [Theory]
    [InlineData("basic YWRtaW46Yg==")]       // lowercase scheme
    [InlineData("  Basic YWRtaW46Yg==")]     // leading whitespace
    [InlineData("Basic  YWRtaW46Yg==")]      // double space after scheme
    public void Parse_CaseInsensitiveSchemeAndLeadingWhitespace(string header)
    {
        Assert.NotNull(BasicAuthParser.TryParse(header));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer abc")]              // wrong scheme
    [InlineData("Basic")]                   // missing value
    [InlineData("Basic ")]                  // empty value
    [InlineData("Basic !!!not-base64")]     // garbage
    [InlineData("Basic bm9jb2xvbg==")]      // "nocolon" → no colon in decoded payload
    public void Parse_Malformed_ReturnsNull(string? header)
    {
        Assert.Null(BasicAuthParser.TryParse(header));
    }

    [Fact]
    public void Parse_EmptyUser_IsRejected()
    {
        // ":password" has empty user
        var header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":password"));
        Assert.Null(BasicAuthParser.TryParse(header));
    }

    [Fact]
    public void Parse_EmptyPassword_IsAccepted()
    {
        // "user:" is a legitimate Basic form (empty password).
        var header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:"));
        var parsed = BasicAuthParser.TryParse(header);
        Assert.NotNull(parsed);
        Assert.Equal("user", parsed.Value.user);
        Assert.Equal("", parsed.Value.password);
    }

    [Fact]
    public void Parse_PasswordContainingColon_PreservesIt()
    {
        var header = Encode("user", "pass:with:colons");
        var parsed = BasicAuthParser.TryParse(header);
        Assert.NotNull(parsed);
        Assert.Equal("pass:with:colons", parsed.Value.password);
    }

    [Fact]
    public void Parse_UnicodeCredentials_Utf8Decoded()
    {
        var header = Encode("用户", "密码é");
        var parsed = BasicAuthParser.TryParse(header);
        Assert.NotNull(parsed);
        Assert.Equal("用户", parsed.Value.user);
        Assert.Equal("密码é", parsed.Value.password);
    }
}
