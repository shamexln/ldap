using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Tests;

public class UacFlagsTests
{
    [Theory]
    [InlineData(0x0200, true)]   // NORMAL_ACCOUNT = enabled
    [InlineData(0x0202, false)]  // NORMAL_ACCOUNT + ACCOUNTDISABLE
    [InlineData(0x0002, false)]  // ACCOUNTDISABLE only
    [InlineData(0x0210, true)]   // NORMAL_ACCOUNT + LOCKOUT (locked != disabled)
    [InlineData(0x0000, true)]   // no flags → treated as enabled
    public void IsEnabled_MatchesBitwiseExpectations(int uac, bool expected)
    {
        Assert.Equal(expected, UacFlags.IsEnabled(uac));
    }

    [Fact]
    public void IsDisabled_IsInverseOfIsEnabled()
    {
        for (int uac = 0; uac < 0x1000; uac++)
        {
            Assert.NotEqual(UacFlags.IsEnabled(uac), UacFlags.IsDisabled(uac));
        }
    }
}
