using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// ADR-0002 §8.2 fix: storage-side contract for multi-step auth sessions.
/// Lets <see cref="ImprivataProxy.IdpCore.Sessions.AuthSessionStore"/> stay
/// in IdpCore (the session *policy* — TTL, cleanup cadence, serverState
/// generation) while EF / DbContext concerns live fully inside Sources.
/// </summary>
public interface IAuthSessionRepo
{
    Task AddAsync(AuthSession session, CancellationToken ct);

    Task<AuthSession?> FindAsync(string serverState, CancellationToken ct);

    Task RemoveAsync(AuthSession session, CancellationToken ct);

    /// <summary>Delete sessions whose <c>ExpiresAt</c> is older than <paramref name="cutoff"/>.</summary>
    Task DeleteExpiredAsync(DateTime cutoff, CancellationToken ct);

    /// <summary>Commit any staged changes from Add/Remove/DeleteExpired in one SaveChanges call.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
