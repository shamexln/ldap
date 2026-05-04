using ImprivataProxy.IdpCore.Authentication;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>Storage-level snapshot of lockout fields for one credential kind.</summary>
public record LockoutState(int FailCount, DateTime? LockedUntil);

/// <summary>
/// ADR-0002 §8.2 fix: storage-side contract for lockout counters + timestamps.
/// <see cref="ImprivataProxy.IdpCore.Authentication.LockoutPolicy"/> reads and writes
/// these without any direct AppDbContext dependency.
/// </summary>
public interface ILockoutRepo
{
    /// <summary>Returns the current counter + locked-until timestamp for the given user + credential kind, or null if the user doesn't exist.</summary>
    Task<LockoutState?> ReadAsync(string userId, PwdOrPin kind, CancellationToken ct);

    /// <summary>Writes the counter + timestamp atomically for the given user + credential kind.</summary>
    Task WriteAsync(
        string userId, PwdOrPin kind,
        int failCount, DateTime? lockedUntil, CancellationToken ct);
}
