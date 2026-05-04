using System.Security.Cryptography;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.IdentityModel.Tokens;

namespace ImprivataProxy.Tests.Helpers;

/// <summary>
/// Generates an ephemeral 2048-bit RSA key for use inside a single test.
/// Disposed alongside the test fixture.
/// </summary>
public sealed class TestSigningKeyProvider : ISigningKeyProvider, IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    public SigningCredentials SigningCredentials { get; }
    public SecurityKey ValidationKey { get; }

    public TestSigningKeyProvider()
    {
        var key = new RsaSecurityKey(_rsa) { KeyId = "test" };
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        ValidationKey = key;
    }

    public void Dispose() => _rsa.Dispose();
}
