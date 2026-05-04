using Microsoft.IdentityModel.Tokens;

namespace ImprivataProxy.IdpCore.Tokens;

public interface ISigningKeyProvider
{
    /// <summary>RSA credentials used to sign newly issued tickets (RS256).</summary>
    SigningCredentials SigningCredentials { get; }

    /// <summary>RSA key used to verify incoming tickets.</summary>
    SecurityKey ValidationKey { get; }
}
