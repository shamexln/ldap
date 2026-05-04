using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Sources.Local;

/// <summary>EF Core implementation of <see cref="ITicketBlacklistRepo"/>.</summary>
public class EfTicketBlacklistRepo : ITicketBlacklistRepo
{
    private readonly AppDbContext _db;

    public EfTicketBlacklistRepo(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(string jti, CancellationToken ct) =>
        _db.TicketBlacklist.AnyAsync(b => b.Jti == jti, ct);

    public Task AddAsync(TicketBlacklistEntry entry, CancellationToken ct)
    {
        _db.TicketBlacklist.Add(entry);
        return Task.CompletedTask;
    }

    public async Task DeleteExpiredAsync(DateTime cutoff, CancellationToken ct)
    {
        var expired = await _db.TicketBlacklist
            .Where(b => b.ExpiresAt < cutoff)
            .ToListAsync(ct);
        if (expired.Count > 0) _db.TicketBlacklist.RemoveRange(expired);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
