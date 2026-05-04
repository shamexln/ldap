using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Sources.Local;

/// <summary>EF Core implementation of <see cref="IAuthSessionRepo"/>.</summary>
public class EfAuthSessionRepo : IAuthSessionRepo
{
    private readonly AppDbContext _db;

    public EfAuthSessionRepo(AppDbContext db) => _db = db;

    public Task AddAsync(AuthSession session, CancellationToken ct)
    {
        _db.AuthSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<AuthSession?> FindAsync(string serverState, CancellationToken ct) =>
        _db.AuthSessions.FirstOrDefaultAsync(s => s.ServerState == serverState, ct);

    public Task RemoveAsync(AuthSession session, CancellationToken ct)
    {
        _db.AuthSessions.Remove(session);
        return Task.CompletedTask;
    }

    public async Task DeleteExpiredAsync(DateTime cutoff, CancellationToken ct)
    {
        var expired = await _db.AuthSessions
            .Where(s => s.ExpiresAt < cutoff)
            .ToListAsync(ct);
        if (expired.Count > 0) _db.AuthSessions.RemoveRange(expired);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
