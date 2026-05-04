using System.Security.Cryptography;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.IdpCore.Sessions;

/// <summary>
/// ADR-0002 §8.2 fix: IdpCore-level <em>policy</em> for multi-step auth sessions.
/// Generates the opaque serverState (128-bit entropy), applies TTL, and triggers
/// opportunistic cleanup. All persistence goes through <see cref="IAuthSessionRepo"/>
/// so this class has no direct <c>AppDbContext</c> dependency.
/// </summary>
public class AuthSessionStore : IAuthSessionStore
{
    private readonly IAuthSessionRepo _repo;

    public AuthSessionStore(IAuthSessionRepo repo) => _repo = repo;

    public async Task<string> CreateAsync(
        string userId, string stage, string pendingModality,
        TimeSpan ttl, CancellationToken ct)
    {
        // 128 bits of entropy → 32 hex chars, unguessable.
        var serverState = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var now = DateTime.UtcNow;
        await _repo.AddAsync(new AuthSession
        {
            ServerState = serverState,
            UserId = userId,
            Stage = stage,
            PendingModality = pendingModality,
            CreatedAt = now,
            ExpiresAt = now + ttl,
        }, ct);

        // Opportunistic cleanup: purge sessions that expired more than a minute ago.
        await _repo.DeleteExpiredAsync(now.AddMinutes(-1), ct);

        await _repo.SaveChangesAsync(ct);
        return serverState;
    }

    public async Task<AuthSession?> GetActiveAsync(string serverState, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(serverState)) return null;
        var session = await _repo.FindAsync(serverState, ct);
        if (session is null) return null;
        if (session.ExpiresAt <= DateTime.UtcNow) return null;
        return session;
    }

    public async Task DeleteAsync(string serverState, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(serverState)) return;
        var session = await _repo.FindAsync(serverState, ct);
        if (session is null) return;
        await _repo.RemoveAsync(session, ct);
        await _repo.SaveChangesAsync(ct);
    }
}
