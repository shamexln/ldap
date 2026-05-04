namespace ImprivataProxy.IdpCore.Authentication;

public interface IUidAuthenticator
{
    Task<AuthResult> AuthenticateAsync(string cardUid, CancellationToken ct);
}
