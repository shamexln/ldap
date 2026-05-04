using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Tests;

public class PwdAuthenticatorTests
{
    /// <summary>
    /// In-memory LDAP stub with a configurable mapping of DN+password → success.
    /// </summary>
    private sealed class FakeLdap : ILdapClient
    {
        public Dictionary<(string dn, string pwd), bool> BindResults { get; } = new();
        public List<(string dn, string pwd)> BindCalls { get; } = new();
        public Exception? ThrowOnBind { get; set; }

        public Task<bool> BindAsUserAsync(string userDn, string password, CancellationToken ct)
        {
            BindCalls.Add((userDn, password));
            if (ThrowOnBind is not null) throw ThrowOnBind;
            return Task.FromResult(BindResults.GetValueOrDefault((userDn, password), false));
        }

        public async IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public TestDbContext Ctx { get; }
        public UserStore Store { get; }
        public FakeLdap Ldap { get; } = new();
        public PasswordHasher Hasher { get; } = new();
        public FakeTicketIssuer Tickets { get; } = new();
        public AuditLogSink Audit { get; }
        public AuthPolicyConfig Policy { get; }
        public PwdAuthenticator Auth { get; }

        public Fixture(AuthPolicyConfig? policyOverride = null)
        {
            Ctx = new TestDbContext();
            Store = new UserStore(Ctx.Db);
            Audit = new AuditLogSink(new EfAuditStore(Ctx.Db));
            Policy = policyOverride ?? new AuthPolicyConfig
            {
                PwdMaxFails = 3,
                PwdLockoutMinutes = 15,
                PwdHashTtlDays = 7,
            };
            Auth = new PwdAuthenticator(
                Store, Ldap, Hasher, Tickets, Audit,
                Options.Create(Policy),
                NullLogger<PwdAuthenticator>.Instance);
        }

        public async Task<User> SeedUserAsync(
            string username = "alice",
            string domain = "CORP",
            bool enabled = true,
            string? dn = "CN=alice,OU=Users,DC=corp,DC=com",
            string? pwdHash = null,
            DateTime? pwdHashUpdatedAt = null)
        {
            var u = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                Domain = domain,
                AdObjectGuid = Guid.NewGuid().ToString(),
                AdDistinguishedName = dn,
                Enabled = enabled,
                PwdHash = pwdHash,
                PwdHashUpdatedAt = pwdHashUpdatedAt,
            };
            Ctx.Db.Users.Add(u);
            await Ctx.Db.SaveChangesAsync();
            return u;
        }

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public async Task Authenticate_FirstLogin_BindsToAd_AndCachesHash()
    {
        using var f = new Fixture();
        var u = await f.SeedUserAsync();
        f.Ldap.BindResults[(u.AdDistinguishedName!, "pwd1")] = true;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var success = Assert.IsType<AuthResult.Success>(result);
        Assert.Equal(u.Id, success.User.Id);
        Assert.NotEmpty(success.Ticket);

        // DB should now carry the cached hash.
        var reloaded = f.Ctx.Db.Users.Single();
        Assert.NotNull(reloaded.PwdHash);
        Assert.NotNull(reloaded.PwdHashUpdatedAt);
        Assert.True(f.Hasher.Verify("pwd1", reloaded.PwdHash!));
    }

    [Fact]
    public async Task Authenticate_LocalHashHit_DoesNotCallLdap()
    {
        using var f = new Fixture();
        var phc = f.Hasher.Hash("pwd1");
        await f.SeedUserAsync(pwdHash: phc, pwdHashUpdatedAt: DateTime.UtcNow);

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        Assert.IsType<AuthResult.Success>(result);
        Assert.Empty(f.Ldap.BindCalls);          // fast path
    }

    [Fact]
    public async Task Authenticate_LocalHashExpired_TriggersBind()
    {
        using var f = new Fixture();
        var phc = f.Hasher.Hash("pwd1");
        var u = await f.SeedUserAsync(
            pwdHash: phc,
            pwdHashUpdatedAt: DateTime.UtcNow.AddDays(-30));  // past TTL (default 7 days)
        f.Ldap.BindResults[(u.AdDistinguishedName!, "pwd1")] = true;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        Assert.IsType<AuthResult.Success>(result);
        Assert.Single(f.Ldap.BindCalls);         // bind happened
        var reloaded = f.Ctx.Db.Users.Single();
        Assert.True(reloaded.PwdHashUpdatedAt > DateTime.UtcNow.AddMinutes(-1));  // refreshed
    }

    [Fact]
    public async Task Authenticate_AdPasswordChanged_LocalHashReplaced()
    {
        using var f = new Fixture();
        // Old hash still fresh by TTL, but password was changed in AD.
        var oldPhc = f.Hasher.Hash("old-pwd");
        var u = await f.SeedUserAsync(
            pwdHash: oldPhc,
            pwdHashUpdatedAt: DateTime.UtcNow);
        f.Ldap.BindResults[(u.AdDistinguishedName!, "new-pwd")] = true;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "new-pwd", default);

        Assert.IsType<AuthResult.Success>(result);
        var reloaded = f.Ctx.Db.Users.Single();
        Assert.True(f.Hasher.Verify("new-pwd", reloaded.PwdHash!));
        Assert.False(f.Hasher.Verify("old-pwd", reloaded.PwdHash!));
    }

    [Fact]
    public async Task Authenticate_WrongPassword_IncrementsFailCount()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "bad", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidCredentials, fail.Rtc);
        Assert.Equal(1, f.Ctx.Db.Users.Single().PwdFailCount);
    }

    [Fact]
    public async Task Authenticate_HitsMaxFails_LocksAndReturnsLockedRtc()
    {
        using var f = new Fixture(new AuthPolicyConfig
        {
            PwdMaxFails = 2,
            PwdLockoutMinutes = 15,
            PwdHashTtlDays = 7,
        });
        await f.SeedUserAsync();

        await f.Auth.AuthenticateAsync("alice", "CORP", "bad", default);
        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "bad", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcAccountLocked, fail.Rtc);
        var reloaded = f.Ctx.Db.Users.Single();
        Assert.NotNull(reloaded.PwdLockedUntil);
        Assert.True(reloaded.PwdLockedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task Authenticate_LockedAccount_RejectsBeforeAnyVerify()
    {
        using var f = new Fixture();
        var u = await f.SeedUserAsync();
        u.PwdLockedUntil = DateTime.UtcNow.AddMinutes(5);
        await f.Ctx.Db.SaveChangesAsync();
        f.Ldap.BindResults[(u.AdDistinguishedName!, "pwd1")] = true;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcAccountLocked, fail.Rtc);
        Assert.Empty(f.Ldap.BindCalls);
    }

    [Fact]
    public async Task Authenticate_UnknownUser_ReturnsInvalidCredentials_NotEnumeration()
    {
        using var f = new Fixture();

        var result = await f.Auth.AuthenticateAsync("ghost", "CORP", "anything", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidCredentials, fail.Rtc);
    }

    [Fact]
    public async Task Authenticate_DisabledUser_ReturnsInvalidCredentials()
    {
        using var f = new Fixture();
        await f.SeedUserAsync(enabled: false);

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidCredentials, fail.Rtc);
    }

    [Fact]
    public async Task Authenticate_SuccessResetsFailCount()
    {
        using var f = new Fixture();
        var u = await f.SeedUserAsync();
        u.PwdFailCount = 2;
        await f.Ctx.Db.SaveChangesAsync();
        f.Ldap.BindResults[(u.AdDistinguishedName!, "pwd1")] = true;

        await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        Assert.Equal(0, f.Ctx.Db.Users.Single().PwdFailCount);
    }

    [Fact]
    public async Task Authenticate_LdapThrows_ReturnsSystemError_DoesNotLock()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();
        f.Ldap.ThrowOnBind = new InvalidOperationException("LDAP down");

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcSystemError, fail.Rtc);
        Assert.Equal(0, f.Ctx.Db.Users.Single().PwdFailCount);    // no counter bump on system error
    }
}
