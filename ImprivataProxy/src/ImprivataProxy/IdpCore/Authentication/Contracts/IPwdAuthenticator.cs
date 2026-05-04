namespace ImprivataProxy.IdpCore.Authentication;

public interface IPwdAuthenticator
{
    Task<AuthResult> AuthenticateAsync(
        string username, string domain, string password, CancellationToken ct);
}
