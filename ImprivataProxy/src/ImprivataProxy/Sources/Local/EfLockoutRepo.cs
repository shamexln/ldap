using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.Sources.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Sources.Local;

/// <summary>EF Core implementation of <see cref="ILockoutRepo"/>.</summary>
public class EfLockoutRepo : ILockoutRepo
{
    private readonly AppDbContext _db;

    public EfLockoutRepo(AppDbContext db) => _db = db;

    public async Task<LockoutState?> ReadAsync(string userId, PwdOrPin kind, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        return kind switch
        {
            PwdOrPin.Pwd => new LockoutState(user.PwdFailCount, user.PwdLockedUntil),
            PwdOrPin.Pin => new LockoutState(user.PinFailCount, user.PinLockedUntil),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public async Task WriteAsync(
        string userId, PwdOrPin kind,
        int failCount, DateTime? lockedUntil, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        switch (kind)
        {
            case PwdOrPin.Pwd:
                user.PwdFailCount = failCount;
                user.PwdLockedUntil = lockedUntil;
                break;
            case PwdOrPin.Pin:
                user.PinFailCount = failCount;
                user.PinLockedUntil = lockedUntil;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
