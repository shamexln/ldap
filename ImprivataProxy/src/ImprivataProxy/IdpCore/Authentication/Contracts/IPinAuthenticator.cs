namespace ImprivataProxy.IdpCore.Authentication;

public interface IPinAuthenticator
{
    Task<AuthResult> AuthenticateAsync(string serverState, string pin, CancellationToken ct);
}
