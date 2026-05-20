using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ImprivataProxy.Tests.Helpers;

/// <summary>
/// WebApplicationFactory that wires ImprivataProxy with:
///   - per-test SQLite in-memory DB (connection owned by the factory)
///   - FakeLdapClient (no real AD)
///   - ADMIN_PASSWORD env var set to a known value
///   - Ticket signing key generated into a throwaway temp dir
/// Expose the FakeLdap so tests can pre-seed users + bind expectations.
/// </summary>
public sealed class IntegrationAppFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "test-admin-password";
    public FakeLdapClient Ldap { get; } = new();

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly string _keyDir;

    public IntegrationAppFactory()
    {
        _conn.Open();
        _keyDir = Path.Combine(Path.GetTempPath(), "imprivata-proxy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_keyDir);

        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", AdminPassword);
        Environment.SetEnvironmentVariable("AD_SVC_PASSWORD", "unused-in-tests");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ticket:SigningKeyPath"] = Path.Combine(_keyDir, "ticket-signing.pem"),
                ["Ticket:Issuer"] = "imprivata-proxy",
                ["Ticket:TtlHours"] = "1",
                ["Database:ConnectionString"] = "Data Source=:memory:",    // overridden below
                ["Ad:UidMode"] = "Card",  // use local card store, not badge AD search
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the SQLite DbContext registration with one pointing at our
            // long-lived in-memory connection so schema + data survive for the test.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_conn));

            // Swap the real LdapClient for the fake — registered under ALL interfaces
            // since it implements ILdapClient (for AD sync), IRemotePasswordVerifier
            // (for PwdAuthenticator), IOnDemandLoginProvider, and IBadgeSearchProvider.
            services.RemoveAll<ILdapClient>();
            services.RemoveAll<IRemotePasswordVerifier>();
            services.RemoveAll<IOnDemandLoginProvider>();
            services.RemoveAll<IBadgeSearchProvider>();
            services.AddSingleton<ILdapClient>(Ldap);
            services.AddSingleton<IRemotePasswordVerifier>(Ldap);
            services.AddSingleton<IOnDemandLoginProvider>(Ldap);
            services.AddSingleton<IBadgeSearchProvider>(Ldap);

            // Force local card-store UID authenticator (not BadgeUidAuthenticator which
            // needs real AD). Program.cs reads config before test overrides apply.
            services.RemoveAll<IUidAuthenticator>();
            services.AddScoped<IUidAuthenticator, UidAuthenticator>();

            // Make sure the AD sync background service never actually runs during tests;
            // remove the hosted-service registration but keep AdSyncService resolvable
            // so SyncController's dependency is satisfied.
            var hosted = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationFactory is not null)
                .ToList();
            foreach (var svc in hosted) services.Remove(svc);
        });
    }

    /// <summary>Creates a scope and gives the caller direct DB access for test seeding.</summary>
    public IServiceScope CreateDbScope() => Services.CreateScope();

    public async Task SeedUserAsync(
        string id, string username, string domain,
        string? dn = null,
        bool enabled = true,
        string? pwdPlaintext = null,
        string? pinPlaintext = null,
        string? cardUidPlaintext = null,
        string? displayName = null,
        string[]? groups = null)
    {
        using var scope = CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // pwdPlaintext is kept as a parameter for backward-compat of test call sites
        // but is no longer stored locally — AD bind is always used at runtime.
        // When pwdPlaintext is provided, register it in the FakeLdap VerifyResults
        // so integration tests pass via remote verification.
        var userDn = dn ?? $"CN={username},OU=Users,DC=corp,DC=example,DC=com";
        var user = new Sources.Local.Entities.User
        {
            Id = id,
            Username = username,
            Domain = domain,
            AdDistinguishedName = userDn,
            AdObjectGuid = Guid.NewGuid().ToString(),
            DisplayName = displayName ?? username,
            Enabled = enabled,
            PinHash = pinPlaintext is null ? null : hasher.Hash(pinPlaintext),
            AttributesJson = groups is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(new { groups }),
        };
        db.Users.Add(user);

        if (!string.IsNullOrEmpty(cardUidPlaintext))
        {
            db.UserCards.Add(new Sources.Local.Entities.UserCard
            {
                Id = Guid.NewGuid().ToString(),
                UserId = user.Id,
                CardUidHash = CardHasher.Hash(cardUidPlaintext),
                CardUidLast4 = CardHasher.Last4(cardUidPlaintext),
                IssuedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        if (pwdPlaintext is not null)
        {
            Ldap.VerifyResults[(userDn, pwdPlaintext)] = RemoteVerifyOutcome.Valid;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _conn.Dispose();
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
        base.Dispose(disposing);
    }
}
