using ImprivataProxy.Sources.Local;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Sources.Contracts;

public interface IUserStore
{
    /// <summary>
    /// Insert a new user or update an existing one matched by AD objectGUID.
    /// Never touches pwd_hash, pin_hash, lockout counters, or user_cards.
    /// </summary>
    Task<UpsertOutcome> UpsertFromAdAsync(AdUserDto dto, CancellationToken ct);

    /// <summary>
    /// Mark as disabled any AD-linked user whose objectGUID is NOT in the given set.
    /// Returns the number of users newly disabled.
    /// </summary>
    Task<int> DisableUsersNotInAsync(HashSet<string> seenObjectGuids, CancellationToken ct);

    /// <summary>
    /// Look up a user for login. Returns null if the user does not exist OR is disabled.
    /// The caller MUST NOT distinguish these two cases to the client (avoid leaking user enumeration).
    /// </summary>
    Task<User?> FindEnabledForLoginAsync(string username, string domain, CancellationToken ct);

    /// <summary>
    /// Look up a user via an enrolled card (by card_uid_hash). Returns null if:
    ///   - no such card,
    ///   - the card has been revoked,
    ///   - the card has expired, or
    ///   - the owning user is disabled.
    /// Caller MUST NOT distinguish these cases to the client.
    /// </summary>
    Task<User?> FindByCardUidHashAsync(string cardUidHash, CancellationToken ct);

    /// <summary>Fetch a user by id (no status filtering — for internal session → user lookup).</summary>
    Task<User?> FindByIdAsync(string userId, CancellationToken ct);

    // ===== ADR-0002 §8.1 fix: Facade must not touch AppDbContext directly. =====
    // Admin controllers and DomainsEndpoint call these instead of EF LINQ on DbContext.

    /// <summary>List users with optional search + enabled filter. Caps `take` to a safe maximum server-side.</summary>
    Task<IReadOnlyList<User>> ListUsersAsync(
        string? search, bool? enabled, int take, CancellationToken ct);

    /// <summary>Fetch a user by id **including their Cards collection** (tracked = false).</summary>
    Task<User?> FindByIdWithCardsAsync(string userId, CancellationToken ct);

    /// <summary>Toggle the enabled flag. Returns false if user not found.</summary>
    Task<bool> SetUserEnabledAsync(string userId, bool enabled, CancellationToken ct);

    /// <summary>Set (or clear, when <paramref name="pinHash"/> is null) the PIN hash. Also resets PIN lockout counters.</summary>
    Task<bool> SetPinHashAsync(string userId, string? pinHash, CancellationToken ct);

    /// <summary>Reset both PWD and PIN failure counters and lockout timestamps.</summary>
    Task<bool> UnlockUserAsync(string userId, CancellationToken ct);

    /// <summary>Check whether a card with this hash already exists (for uniqueness validation).</summary>
    Task<UserCard?> FindCardByHashAsync(string cardUidHash, CancellationToken ct);

    /// <summary>Insert a new card row.</summary>
    Task CreateCardAsync(UserCard card, CancellationToken ct);

    /// <summary>Fetch a card by id; includes <c>User</c> navigation for audit context.</summary>
    Task<UserCard?> FindCardByIdWithUserAsync(string cardId, CancellationToken ct);

    /// <summary>Mark a card as revoked. Returns false if not found.</summary>
    Task<bool> RevokeCardAsync(string cardId, CancellationToken ct);

    /// <summary>Distinct domain names for enabled users, alphabetical order. Powers GET /Domains.</summary>
    Task<IReadOnlyList<string>> GetDistinctEnabledDomainsAsync(CancellationToken ct);
}
