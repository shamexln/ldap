using ImprivataProxy.Configuration;
using ImprivataProxy.Sources.Contracts;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

/// <summary>
/// Default <see cref="ILockoutPolicy"/>. Reads max-fails + lockout-duration from
/// <see cref="AuthPolicyConfig"/>; persistence goes through <see cref="ILockoutRepo"/>
/// so this class holds no DbContext dependency.
/// </summary>
public class LockoutPolicy : ILockoutPolicy
{
    private readonly ILockoutRepo _repo;
    private readonly AuthPolicyConfig _policy;

    public LockoutPolicy(ILockoutRepo repo, IOptions<AuthPolicyConfig> policy)
    {
        _repo = repo;
        _policy = policy.Value;
    }

    public async Task<LockoutStatus> CheckAsync(string userId, PwdOrPin kind, CancellationToken ct)
    {
        var state = await _repo.ReadAsync(userId, kind, ct);
        if (state is null) return LockoutStatus.Unlocked;

        var isLocked = state.LockedUntil is { } until && until > DateTime.UtcNow;
        return new LockoutStatus(isLocked, state.LockedUntil, state.FailCount);
    }

    public async Task<LockoutStatus> OnSuccessAsync(string userId, PwdOrPin kind, CancellationToken ct)
    {
        var state = await _repo.ReadAsync(userId, kind, ct);
        if (state is null) return LockoutStatus.Unlocked;

        // Idempotent: skip the write when already clean.
        if (state.FailCount == 0 && state.LockedUntil is null) return LockoutStatus.Unlocked;

        await _repo.WriteAsync(userId, kind, 0, null, ct);
        return LockoutStatus.Unlocked;
    }

    public async Task<LockoutStatus> OnFailureAsync(string userId, PwdOrPin kind, CancellationToken ct)
    {
        var state = await _repo.ReadAsync(userId, kind, ct);
        if (state is null) return LockoutStatus.Unlocked;

        var (maxFails, duration) = kind switch
        {
            PwdOrPin.Pwd => (_policy.PwdMaxFails, TimeSpan.FromMinutes(_policy.PwdLockoutMinutes)),
            PwdOrPin.Pin => (_policy.PinMaxFails, TimeSpan.FromMinutes(_policy.PinLockoutMinutes)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var newCount = state.FailCount + 1;
        DateTime? lockedUntil = state.LockedUntil;
        if (newCount >= maxFails && lockedUntil is null)
        {
            lockedUntil = DateTime.UtcNow + duration;
        }

        await _repo.WriteAsync(userId, kind, newCount, lockedUntil, ct);

        var isLocked = lockedUntil is { } until && until > DateTime.UtcNow;
        return new LockoutStatus(isLocked, lockedUntil, newCount);
    }
}
