using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.IdpCore.Tokens;

/// <summary>
/// ADR-0002 §8.2 fix: IdpCore-level <em>policy</em> for revoked OStick tickets.
/// Handles dedup on Add and opportunistic GC of expired entries. Delegates all
/// persistence to <see cref="ITicketBlacklistRepo"/> so no DbContext dependency
/// leaks into IdpCore.
/// </summary>
public class TicketBlacklistService : ITicketBlacklist
{
    private readonly ITicketBlacklistRepo _repo;

    public TicketBlacklistService(ITicketBlacklistRepo repo) => _repo = repo;

    public async Task AddAsync(string jti, DateTime expiresAt, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(jti)) return;

        if (!await _repo.ExistsAsync(jti, ct))
        {
            await _repo.AddAsync(new TicketBlacklistEntry
            {
                Jti = jti,
                RevokedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
            }, ct);
        }

        // Opportunistic cleanup: remove entries that have already expired.
        // The JWT itself is also rejected by signature/exp validation, but we
        // don't want the table to grow forever.
        await _repo.DeleteExpiredAsync(DateTime.UtcNow.AddMinutes(-5), ct);

        await _repo.SaveChangesAsync(ct);
    }

    public Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(jti)) return Task.FromResult(false);
        return _repo.ExistsAsync(jti, ct);
    }
}
