namespace ImprivataProxy.Sources.ActiveDirectory;

/// <summary>
/// AD-specific read access: paged search over the configured OU.
/// Password verification lives under
/// <see cref="ImprivataProxy.Sources.Contracts.IRemotePasswordVerifier"/>
/// so that IdpCore stays protocol-neutral (ADR-0002 §4.1).
/// </summary>
public interface ILdapClient
{
    /// <summary>
    /// Runs a paged search for all users under the configured base DN.
    /// Yields each result as AdUserDto. Throws on connection/bind failure.
    /// </summary>
    IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(CancellationToken ct);

    /// <summary>
    /// OnDemand mode: bind with user credentials (UPN format), then search for own attributes.
    /// Returns Valid + AdUserDto on success, Invalid on bad credentials, Unreachable on errors.
    /// </summary>
    Task<OnDemandLoginResult> BindAndSearchSelfAsync(
        string username, string domain, string password, CancellationToken ct);
}
