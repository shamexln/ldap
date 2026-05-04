using System.Text.Json;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.ActiveDirectory;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Sources.Local;

public class UserStore : IUserStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AppDbContext _db;

    public UserStore(AppDbContext db) => _db = db;

    public async Task<UpsertOutcome> UpsertFromAdAsync(AdUserDto dto, CancellationToken ct)
    {
        var guidStr = dto.ObjectGuid.ToString();
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.AdObjectGuid == guidStr, ct);

        var attributes = JsonSerializer.Serialize(new
        {
            mail = dto.Mail,
            groups = dto.Groups,
        }, JsonOpts);

        if (existing is null)
        {
            _db.Users.Add(new Entities.User
            {
                Id = Guid.NewGuid().ToString(),
                Username = dto.Username,
                Domain = dto.Domain,
                AdObjectGuid = guidStr,
                AdDistinguishedName = dto.DistinguishedName,
                DisplayName = dto.DisplayName,
                PwdHash = null,
                PinHash = null,
                Enabled = dto.Enabled,
                AttributesJson = attributes,
                LastSyncedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
            return UpsertOutcome.Inserted;
        }

        // Update: never touch pwd_hash, pin_hash, lockout counters, user_cards
        var changed =
            existing.Username != dto.Username ||
            existing.Domain != dto.Domain ||
            existing.AdDistinguishedName != dto.DistinguishedName ||
            existing.DisplayName != dto.DisplayName ||
            existing.Enabled != dto.Enabled ||
            existing.AttributesJson != attributes;

        existing.Username = dto.Username;
        existing.Domain = dto.Domain;
        existing.AdDistinguishedName = dto.DistinguishedName;
        existing.DisplayName = dto.DisplayName;
        existing.Enabled = dto.Enabled;
        existing.AttributesJson = attributes;
        existing.LastSyncedAt = DateTime.UtcNow;

        if (changed)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return UpsertOutcome.Updated;
        }

        // Always persist LastSyncedAt even if nothing else changed.
        await _db.SaveChangesAsync(ct);
        return UpsertOutcome.Unchanged;
    }

    public async Task<int> DisableUsersNotInAsync(HashSet<string> seenObjectGuids, CancellationToken ct)
    {
        // Only operate on AD-linked users that are currently enabled.
        var adLinked = await _db.Users
            .Where(u => u.AdObjectGuid != null && u.Enabled)
            .Select(u => new { u.Id, u.AdObjectGuid })
            .ToListAsync(ct);

        var toDisable = adLinked
            .Where(u => u.AdObjectGuid != null && !seenObjectGuids.Contains(u.AdObjectGuid))
            .Select(u => u.Id)
            .ToList();

        if (toDisable.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var id in toDisable)
        {
            var user = await _db.Users.FirstAsync(u => u.Id == id, ct);
            user.Enabled = false;
            user.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return toDisable.Count;
    }

    public Task<Entities.User?> FindEnabledForLoginAsync(
        string username, string domain, CancellationToken ct)
    {
        return _db.Users
            .Where(u => u.Username == username && u.Domain == domain && u.Enabled)
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdatePwdHashAsync(string userId, string pwdHash, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;
        user.PwdHash = pwdHash;
        user.PwdHashUpdatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public Task<Entities.User?> FindByCardUidHashAsync(string cardUidHash, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cardUidHash)) return Task.FromResult<Entities.User?>(null);

        var now = DateTime.UtcNow;
        return _db.UserCards
            .Where(c => c.CardUidHash == cardUidHash
                     && !c.Revoked
                     && (c.ExpiresAt == null || c.ExpiresAt > now)
                     && c.User.Enabled)
            .Select(c => c.User)
            .FirstOrDefaultAsync(ct);
    }

    public Task<Entities.User?> FindByIdAsync(string userId, CancellationToken ct)
    {
        return _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    // ===== ADR-0002 §8.1 fix: Admin + DomainsEndpoint data access moved here =====

    public async Task<IReadOnlyList<Entities.User>> ListUsersAsync(
        string? search, bool? enabled, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 500);

        var q = _db.Users.AsNoTracking().Include(u => u.Cards).AsQueryable();
        if (enabled is not null) q = q.Where(u => u.Enabled == enabled.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(u =>
                u.Username.Contains(search) ||
                (u.DisplayName != null && u.DisplayName.Contains(search)));
        }

        return await q
            .OrderBy(u => u.Domain).ThenBy(u => u.Username)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<Entities.User?> FindByIdWithCardsAsync(string userId, CancellationToken ct)
    {
        return _db.Users.AsNoTracking()
            .Include(u => u.Cards)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    public async Task<bool> SetUserEnabledAsync(string userId, bool enabled, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;
        user.Enabled = enabled;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetPinHashAsync(string userId, string? pinHash, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;
        user.PinHash = pinHash;
        user.PinFailCount = 0;
        user.PinLockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UnlockUserAsync(string userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;
        user.PwdFailCount = 0;
        user.PwdLockedUntil = null;
        user.PinFailCount = 0;
        user.PinLockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<Entities.UserCard?> FindCardByHashAsync(string cardUidHash, CancellationToken ct)
    {
        return _db.UserCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CardUidHash == cardUidHash, ct);
    }

    public async Task CreateCardAsync(Entities.UserCard card, CancellationToken ct)
    {
        _db.UserCards.Add(card);
        await _db.SaveChangesAsync(ct);
    }

    public Task<Entities.UserCard?> FindCardByIdWithUserAsync(string cardId, CancellationToken ct)
    {
        return _db.UserCards
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct);
    }

    public async Task<bool> RevokeCardAsync(string cardId, CancellationToken ct)
    {
        var card = await _db.UserCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null) return false;
        card.Revoked = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> GetDistinctEnabledDomainsAsync(CancellationToken ct)
    {
        return await _db.Users
            .Where(u => u.Enabled)
            .Select(u => u.Domain)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);
    }
}
