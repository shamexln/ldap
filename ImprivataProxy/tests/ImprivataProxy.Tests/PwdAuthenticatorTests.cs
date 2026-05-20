using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;
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
    private sealed class FakeVerifier : IRemotePasswordVerifier
    {
        public Dictionary<(string dn, string pwd), RemoteVerifyOutcome> Results { get; } = new();
        public List<(string dn, string pwd)> Calls { get; } = new();

        public Task<RemoteVerifyResult> VerifyAsync(
            UserIdentity identity, string password, CancellationToken ct)
        {
            var dn = identity.DistinguishedName ?? "";
            Calls.Add((dn, password));
            var outcome = Results.GetValueOrDefault((dn, password), RemoteVerifyOutcome.Invalid);
            return Task.FromResult(new RemoteVerifyResult(outcome));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public TestDbContext Ctx { get; }
        public UserStore Store { get; }
        public FakeVerifier Remote { get; } = new();
        public FakeLdapClient Ldap { get; } = new();
        public FakeTicketIssuer Tickets { get; } = new();
        public AuditLogSink Audit { get; }
        public AuthPolicyConfig Policy { get; }
        public AdConfig AdCfg { get; }
        public PwdAuthenticator Auth { get; }

        public Fixture(AuthPolicyConfig? policyOverride = null, AdConfig? adConfigOverride = null)
        {
            Ctx = new TestDbContext();
            Store = new UserStore(Ctx.Db);
            Audit = new AuditLogSink(new EfAuditStore(Ctx.Db));
            Policy = policyOverride ?? new AuthPolicyConfig
            {
                PwdMaxFails = 3,
                PwdLockoutMinutes = 15,
            };
            AdCfg = adConfigOverride ?? new AdConfig { Mode = "Sync" };
            var lockout = new LockoutPolicy(new EfLockoutRepo(Ctx.Db), Options.Create(Policy));
            Auth = new PwdAuthenticator(
                Store, Remote, Ldap, lockout, Tickets, Audit,
                Options.Create(Policy),
                Options.Create(AdCfg),
                NullLogger<PwdAuthenticator>.Instance);
        }

        public async Task<User> SeedUserAsync(
            string username = "alice",
            string domain = "CORP",
            bool enabled = true,
            string? dn = "CN=alice,OU=Users,DC=corp,DC=com")
        {
            var u = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                Domain = domain,
                AdObjectGuid = Guid.NewGuid().ToString(),
                AdDistinguishedName = dn,
                Enabled = enabled,
            };
            Ctx.Db.Users.Add(u);
            await Ctx.Db.SaveChangesAsync();
            return u;
        }

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public async Task Authenticate_ValidPassword_BindsToAd_Succeeds()
    {
        using var f = new Fixture();
        var u = await f.SeedUserAsync();
        f.Remote.Results[(u.AdDistinguishedName!, "pwd1")] = RemoteVerifyOutcome.Valid;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var success = Assert.IsType<AuthResult.Success>(result);
        Assert.Equal(u.Id, success.User.Id);
        Assert.NotEmpty(success.Ticket);
        Assert.Single(f.Remote.Calls);
    }

    [Fact]
    public async Task Authenticate_AlwaysCallsRemote_EvenOnSecondLogin()
    {
        using var f = new Fixture();
        var u = await f.SeedUserAsync();
        f.Remote.Results[(u.AdDistinguishedName!, "pwd1")] = RemoteVerifyOutcome.Valid;

        await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);
        await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        Assert.Equal(2, f.Remote.Calls.Count);
    }

    [Fact]
    public async Task Authenticate_WrongPassword_IncrementsFailCount()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "bad", default);

        var fail = Assert.IsType<AuthResult.CredentialFailure>(result);
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
        });
        await f.SeedUserAsync();

        await f.Auth.AuthenticateAsync("alice", "CORP", "bad", default);
        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "bad", default);

        var fail = Assert.IsType<AuthResult.CredentialFailure>(result);
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
        f.Remote.Results[(u.AdDistinguishedName!, "pwd1")] = RemoteVerifyOutcome.Valid;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcAccountLocked, fail.Rtc);
        Assert.Empty(f.Remote.Calls);
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
        f.Remote.Results[(u.AdDistinguishedName!, "pwd1")] = RemoteVerifyOutcome.Valid;

        await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        Assert.Equal(0, f.Ctx.Db.Users.Single().PwdFailCount);
    }

    [Fact]
    public async Task Authenticate_RemoteUnreachable_ReturnsSystemError_DoesNotLock()
    {
        using var f = new Fixture();
        var u = await f.SeedUserAsync();
        f.Remote.Results[(u.AdDistinguishedName!, "pwd1")] = RemoteVerifyOutcome.Unreachable;

        var result = await f.Auth.AuthenticateAsync("alice", "CORP", "pwd1", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcSystemError, fail.Rtc);
        Assert.Equal(0, f.Ctx.Db.Users.Single().PwdFailCount);
    }
}
