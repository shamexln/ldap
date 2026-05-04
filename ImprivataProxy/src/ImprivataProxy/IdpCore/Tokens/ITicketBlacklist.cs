namespace ImprivataProxy.IdpCore.Tokens;

public interface ITicketBlacklist
{
    /// <summary>Add the JTI to the blacklist. Idempotent: repeated adds are a no-op.</summary>
    Task AddAsync(string jti, DateTime expiresAt, CancellationToken ct);

    /// <summary>Returns true if the JTI is currently blacklisted.</summary>
    Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct);
}
