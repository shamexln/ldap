using System.Security.Cryptography;
using ImprivataProxy.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ImprivataProxy.IdpCore.Tokens;

/// <summary>
/// Loads a PEM-encoded RSA private key from disk, or auto-generates one on first run.
/// Auto-generation is convenient for dev/first-deploy; production should pre-provision
/// a key out-of-band. A warning is always logged when a key is generated.
/// </summary>
public class SigningKeyProvider : ISigningKeyProvider, IDisposable
{
    private readonly RSA _rsa;
    public SigningCredentials SigningCredentials { get; }
    public SecurityKey ValidationKey { get; }

    public SigningKeyProvider(IOptions<TicketConfig> config, ILogger<SigningKeyProvider> logger)
    {
        var path = config.Value.SigningKeyPath;
        _rsa = LoadOrCreate(path, logger);

        var key = new RsaSecurityKey(_rsa) { KeyId = "imprivata-proxy-signing" };
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        ValidationKey = key;
    }

    private static RSA LoadOrCreate(string path, ILogger logger)
    {
        if (File.Exists(path))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(path));
            logger.LogInformation("Loaded ticket signing key from {Path}", path);
            return rsa;
        }

        logger.LogWarning(
            "Ticket signing key not found at {Path}; generating a new 2048-bit RSA key. " +
            "Provision a stable key for production deployments.",
            path);

        var created = RSA.Create(2048);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var pem = created.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(path, pem);

        // Best-effort POSIX restriction; on Windows, rely on NTFS ACLs / DPAPI.
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // non-fatal; just log via caller if needed.
        }

        return created;
    }

    public void Dispose() => _rsa.Dispose();
}
