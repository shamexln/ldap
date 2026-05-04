using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.IdpCore.Tokens;

public class TicketBlacklistService : ITicketBlacklist
{
    private readonly AppDbContext _db;

    public TicketBlacklistService(AppDbContext db) => _db = db;

    public async Task AddAsync(string jti, DateTime expiresAt, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(jti)) return;

        var exists = await _db.TicketBlacklist.AnyAsync(b => b.Jti == jti, ct);
        if (!exists)
        {
            _db.TicketBlacklist.Add(new TicketBlacklistEntry
            {
                Jti = jti,
                RevokedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
            });
        }

        // Opportunistic cleanup: remove entries that have already expired.
        // The JWT itself is also rejected by signature/exp validation, but we
        // don't want the table to grow forever.
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var expired = await _db.TicketBlacklist
            .Where(b => b.ExpiresAt < cutoff)
            .ToListAsync(ct);
        if (expired.Count > 0) _db.TicketBlacklist.RemoveRange(expired);

        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(jti)) return Task.FromResult(false);
        return _db.TicketBlacklist.AnyAsync(b => b.Jti == jti, ct);
    }
}
