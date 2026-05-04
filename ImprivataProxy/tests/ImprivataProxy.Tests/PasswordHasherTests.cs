using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;

namespace ImprivataProxy.Tests;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _h = new PasswordHasher();

    [Fact]
    public void HashVerify_RoundTrips()
    {
        var phc = _h.Hash("correct-horse-battery-staple");
        Assert.True(_h.Verify("correct-horse-battery-staple", phc));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var phc = _h.Hash("secret");
        Assert.False(_h.Verify("SECRET", phc));
        Assert.False(_h.Verify("", phc));
        Assert.False(_h.Verify("wrong", phc));
    }

    [Fact]
    public void Hash_UsesFreshSalt_ForSamePassword()
    {
        var a = _h.Hash("same");
        var b = _h.Hash("same");
        Assert.NotEqual(a, b);          // different salt → different PHC
        Assert.True(_h.Verify("same", a));
        Assert.True(_h.Verify("same", b));
    }

    [Fact]
    public void Hash_PhcFormat_Correct()
    {
        var phc = _h.Hash("x");
        // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", phc);
        Assert.Equal(5, phc.Count(c => c == '$'));
    }

    [Fact]
    public void Verify_MalformedPhc_ReturnsFalse()
    {
        Assert.False(_h.Verify("any", ""));
        Assert.False(_h.Verify("any", "not-a-phc"));
        Assert.False(_h.Verify("any", "$argon2id$v=19$m=0,t=0,p=0$AA$BB"));
        Assert.False(_h.Verify("any", "$argon2i$v=19$m=19456,t=2,p=1$QUFB$QkJC"));  // we don't parse argon2i
    }

    [Fact]
    public void Verify_EmptyPassword_ReturnsFalse_EvenIfHashed()
    {
        // By contract, we early-return false on empty password to keep call sites simple.
        var phc = _h.Hash("real");
        Assert.False(_h.Verify("", phc));
    }

    [Fact]
    public void TryParse_ValidPhc_ExtractsParams()
    {
        var phc = _h.Hash("x");
        var ok = PasswordHasher.TryParse(phc, out var salt, out var hash,
            out var m, out var t, out var p);
        Assert.True(ok);
        Assert.Equal(19456, m);
        Assert.Equal(2, t);
        Assert.Equal(1, p);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, hash.Length);
    }
}
