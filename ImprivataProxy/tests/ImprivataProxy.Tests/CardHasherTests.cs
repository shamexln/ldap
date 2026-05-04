using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;

namespace ImprivataProxy.Tests;

public class CardHasherTests
{
    [Fact]
    public void Hash_Deterministic()
    {
        Assert.Equal(CardHasher.Hash("12345"), CardHasher.Hash("12345"));
    }

    [Fact]
    public void Hash_DifferentUids_DifferentHashes()
    {
        Assert.NotEqual(CardHasher.Hash("12345"), CardHasher.Hash("12346"));
    }

    [Fact]
    public void Hash_TrimsWhitespace()
    {
        Assert.Equal(CardHasher.Hash("12345"), CardHasher.Hash("  12345  "));
        Assert.Equal(CardHasher.Hash("12345"), CardHasher.Hash("\t12345\n"));
    }

    [Fact]
    public void Hash_ProducesLowerHexOf64Chars()
    {
        var hash = CardHasher.Hash("card-1");
        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    [Theory]
    [InlineData("1234567890", "7890")]
    [InlineData("abcd", "abcd")]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    [InlineData("  abcdefg  ", "defg")]
    public void Last4(string uid, string expected)
    {
        Assert.Equal(expected, CardHasher.Last4(uid));
    }
}
