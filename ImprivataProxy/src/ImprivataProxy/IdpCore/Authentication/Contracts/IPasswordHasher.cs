namespace ImprivataProxy.IdpCore.Authentication;

public interface IPasswordHasher
{
    /// <summary>
    /// Produce a PHC-formatted argon2id hash string of the given password.
    /// Generates a fresh random salt each call.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verify a password against a previously produced PHC string.
    /// Returns false on any parse failure or mismatch; never throws.
    /// </summary>
    bool Verify(string password, string phcString);
}
