using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Tokens;

namespace ImprivataProxy.Tests;

public class OStickHeaderTests
{
    [Fact]
    public void Format_RoundTripsWithTryExtract()
    {
        var header = OStickHeader.Format("eyJhbGciOiJSUzI1NiJ9.abc.def");
        var extracted = OStickHeader.TryExtractTicket(header);
        Assert.Equal("eyJhbGciOiJSUzI1NiJ9.abc.def", extracted);
    }

    [Theory]
    [InlineData("OStick ostick.ticket=TKT",              "TKT")]
    [InlineData("ostick ostick.ticket=TKT",              "TKT")]     // case-insensitive scheme
    [InlineData("OSTICK ostick.ticket=TKT",              "TKT")]
    [InlineData("OStick ostick.TICKET=TKT",              "TKT")]     // case-insensitive key
    [InlineData("OStick ostick.ticket=TKT,other=x",      "TKT")]
    [InlineData("OStick ostick.ticket=\"TKT\"",          "TKT")]
    [InlineData("   OStick ostick.ticket=TKT",           "TKT")]
    public void TryExtract_Extracts(string header, string expected)
    {
        Assert.Equal(expected, OStickHeader.TryExtractTicket(header));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Bearer abc")]
    [InlineData("OStick")]
    [InlineData("OStick wrong=TKT")]
    [InlineData("OStick ostick.ticket=")]
    public void TryExtract_Malformed_ReturnsNull(string? header)
    {
        Assert.Null(OStickHeader.TryExtractTicket(header));
    }
}
