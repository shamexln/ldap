using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace ImprivataProxy.IdpCore.Authentication;

/// <summary>
/// Argon2id hasher using PHC string format:
///   $argon2id$v=19$m=19456,t=2,p=1$&lt;saltB64&gt;$&lt;hashB64&gt;
///
/// Parameters per OWASP 2024 recommendation:
///   memorySize (m) = 19456 KiB (19 MB)
///   iterations (t) = 2
///   parallelism (p) = 1
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int MemorySizeKb = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int ArgonVersion = 19;    // argon2 v1.3 encodes as v=19

    public string Hash(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = ComputeHash(password, salt, MemorySizeKb, Iterations, Parallelism, HashBytes);

        return $"$argon2id$v={ArgonVersion}$m={MemorySizeKb},t={Iterations},p={Parallelism}" +
               $"${Base64Url(salt)}${Base64Url(hash)}";
    }

    public bool Verify(string password, string phcString)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(phcString))
        {
            return false;
        }

        if (!TryParse(phcString, out var salt, out var expected, out var m, out var t, out var p))
        {
            return false;
        }

        try
        {
            var actual = ComputeHash(password, salt, m, t, p, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt,
        int memoryKb, int iterations, int parallelism, int hashLen)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memoryKb,
        };
        return argon.GetBytes(hashLen);
    }

    internal static bool TryParse(string phc,
        out byte[] salt, out byte[] hash, out int m, out int t, out int p)
    {
        salt = hash = Array.Empty<byte>();
        m = t = p = 0;

        // Expected: $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
        var parts = phc.Split('$');
        if (parts.Length != 6) return false;
        if (parts[0].Length != 0) return false;       // leading $
        if (parts[1] != "argon2id") return false;
        if (!parts[2].StartsWith("v=")) return false;
        // parts[2] (version) is accepted as-is; we don't branch on it yet.

        foreach (var kv in parts[3].Split(','))
        {
            var eq = kv.IndexOf('=');
            if (eq <= 0) return false;
            var key = kv[..eq];
            var val = kv[(eq + 1)..];
            if (!int.TryParse(val, out var n)) return false;
            switch (key)
            {
                case "m": m = n; break;
                case "t": t = n; break;
                case "p": p = n; break;
                default: return false;
            }
        }
        if (m <= 0 || t <= 0 || p <= 0) return false;

        try
        {
            salt = FromBase64Url(parts[4]);
            hash = FromBase64Url(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length > 0 && hash.Length > 0;
    }

    // PHC uses base64 without padding (aka base64url-nopad, but with standard alphabet).
    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=');

    private static byte[] FromBase64Url(string s)
    {
        var pad = (4 - s.Length % 4) % 4;
        return Convert.FromBase64String(s + new string('=', pad));
    }
}
