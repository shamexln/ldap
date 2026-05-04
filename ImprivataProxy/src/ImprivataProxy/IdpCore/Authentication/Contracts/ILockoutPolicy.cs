using ImprivataProxy.Shared.Contracts;

namespace ImprivataProxy.IdpCore.Authentication;

/// <summary>Snapshot of a credential's lockout state at a point in time.</summary>
public record LockoutStatus(bool IsLocked, DateTime? LockedUntil, int FailCount)
{
    public static LockoutStatus Unlocked { get; } = new(false, null, 0);
}

/// <summary>
/// ADR-0002 §4.2: policy for password / PIN lockout. Authenticators call
/// <see cref="CheckAsync"/> before verifying the credential, <see cref="OnSuccessAsync"/>
/// after a successful verify (to reset counters), and <see cref="OnFailureAsync"/> after
/// a failed verify (to increment counters and potentially lock).
///
/// The policy reads max-fails and lockout-duration from <c>AuthPolicyConfig</c> itself,
/// so authenticators no longer pass those values as arguments.
/// </summary>
public interface ILockoutPolicy
{
    Task<LockoutStatus> CheckAsync(string userId, PwdOrPin kind, CancellationToken ct);

    /// <summary>Reset fail count and cleared lock timestamp. Idempotent when already zero.</summary>
    Task<LockoutStatus> OnSuccessAsync(string userId, PwdOrPin kind, CancellationToken ct);

    /// <summary>Increment fail count; if threshold is reached, set locked-until to now + duration.</summary>
    Task<LockoutStatus> OnFailureAsync(string userId, PwdOrPin kind, CancellationToken ct);
}
