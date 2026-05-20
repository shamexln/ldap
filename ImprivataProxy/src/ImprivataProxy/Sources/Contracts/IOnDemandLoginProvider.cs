using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// Protocol-neutral interface for on-demand login: bind with user credentials and
/// retrieve user attributes in one step. Used by PwdAuthenticator in OnDemand mode.
/// Today implemented by LdapClient (LDAP bind + search); could be backed by any
/// identity source that supports credential verification + attribute retrieval.
/// </summary>
public interface IOnDemandLoginProvider
{
    Task<OnDemandLoginResult> BindAndSearchSelfAsync(
        string username, string domain, string password, CancellationToken ct);
}
