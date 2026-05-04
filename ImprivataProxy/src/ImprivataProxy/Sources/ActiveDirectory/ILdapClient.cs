namespace ImprivataProxy.Sources.ActiveDirectory;

public interface ILdapClient
{
    /// <summary>
    /// Verifies a user's password by binding to AD with their DN + password.
    /// Returns true if bind succeeds, false otherwise.
    /// Used by PwdAuthenticator for the bind-fallback path.
    /// </summary>
    Task<bool> BindAsUserAsync(string userDn, string password, CancellationToken ct);

    /// <summary>
    /// Runs a paged search for all users under the configured base DN.
    /// Yields each result as AdUserDto. Throws on connection/bind failure.
    /// </summary>
    IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(CancellationToken ct);
}
