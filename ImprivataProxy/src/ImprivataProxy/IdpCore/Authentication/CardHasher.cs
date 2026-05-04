using System.Security.Cryptography;
using System.Text;

namespace ImprivataProxy.IdpCore.Authentication;

/// <summary>
/// One-way hashing for card UIDs. Pure, static — no key material required.
/// We use a deterministic hash (not per-card salt) because the UID itself is the
/// lookup key: we must be able to locate the card row from the inbound UID alone.
/// Rainbow-table risk is minimal for card IDs (high entropy, not secret per se).
/// </summary>
public static class CardHasher
{
    /// <summary>
    /// SHA-256 of the UTF-8 encoding of the card UID, lowercase hex.
    /// Leading/trailing whitespace in the input is trimmed so card readers with
    /// stray whitespace don't cause lookup misses.
    /// </summary>
    public static string Hash(string cardUid)
    {
        if (cardUid is null) throw new ArgumentNullException(nameof(cardUid));
        var trimmed = cardUid.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Returns the last 4 characters of the trimmed card UID, for admin display.</summary>
    public static string Last4(string cardUid)
    {
        var trimmed = (cardUid ?? "").Trim();
        return trimmed.Length <= 4 ? trimmed : trimmed[^4..];
    }
}
