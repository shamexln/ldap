using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// Protocol-neutral interface for badge-based user lookup.
/// Today implemented by LdapClient (AD attribute search); could be backed by
/// any directory or badge management system.
/// </summary>
public interface IBadgeSearchProvider
{
    Task<AdUserDto?> SearchByBadgeAsync(string badgeValue, CancellationToken ct);
}
