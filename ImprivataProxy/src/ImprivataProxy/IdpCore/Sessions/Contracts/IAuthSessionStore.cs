using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.IdpCore.Sessions;

public interface IAuthSessionStore
{
    /// <summary>
    /// Create a new multi-step auth session and return the opaque ServerState token.
    /// </summary>
    Task<string> CreateAsync(
        string userId, string stage, string pendingModality,
        TimeSpan ttl, CancellationToken ct);

    /// <summary>
    /// Look up an active (non-expired) session by its ServerState. Returns null if not found
    /// or expired.
    /// </summary>
    Task<AuthSession?> GetActiveAsync(string serverState, CancellationToken ct);

    /// <summary>Delete the session. No-op if it does not exist.</summary>
    Task DeleteAsync(string serverState, CancellationToken ct);
}
