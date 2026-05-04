using System.Security.Cryptography;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.IdpCore.Sessions;

public class AuthSessionStore : IAuthSessionStore
{
    private readonly AppDbContext _db;

    public AuthSessionStore(AppDbContext db) => _db = db;

    public async Task<string> CreateAsync(
        string userId, string stage, string pendingModality,
        TimeSpan ttl, CancellationToken ct)
    {
        // 128 bits of entropy → 32 hex chars, unguessable.
        var serverState = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var now = DateTime.UtcNow;
        _db.AuthSessions.Add(new AuthSession
        {
            ServerState = serverState,
            UserId = userId,
            Stage = stage,
            PendingModality = pendingModality,
            CreatedAt = now,
            ExpiresAt = now + ttl,
        });

        // Opportunistic cleanup: purge sessions that expired more than a minute ago.
        var cutoff = now.AddMinutes(-1);
        var expired = await _db.AuthSessions
            .Where(s => s.ExpiresAt < cutoff)
            .ToListAsync(ct);
        if (expired.Count > 0) _db.AuthSessions.RemoveRange(expired);

        await _db.SaveChangesAsync(ct);
        return serverState;
    }

    public async Task<AuthSession?> GetActiveAsync(string serverState, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(serverState)) return null;
        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.ServerState == serverState, ct);
        if (session is null) return null;
        if (session.ExpiresAt <= DateTime.UtcNow) return null;
        return session;
    }

    public async Task DeleteAsync(string serverState, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(serverState)) return;
        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.ServerState == serverState, ct);
        if (session is null) return;
        _db.AuthSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
    }
}
