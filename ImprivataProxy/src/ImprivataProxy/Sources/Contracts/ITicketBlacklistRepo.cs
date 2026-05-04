using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// ADR-0002 §8.2 fix: storage-side contract for revoked OStick tickets.
/// Keeps <see cref="ImprivataProxy.IdpCore.Tokens.TicketBlacklistService"/>
/// (the *policy* — dedup + opportunistic GC cadence) free of direct DbContext use.
/// </summary>
public interface ITicketBlacklistRepo
{
    Task<bool> ExistsAsync(string jti, CancellationToken ct);

    Task AddAsync(TicketBlacklistEntry entry, CancellationToken ct);

    /// <summary>Delete entries whose <c>ExpiresAt</c> is older than <paramref name="cutoff"/>.</summary>
    Task DeleteExpiredAsync(DateTime cutoff, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
