using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImprivataProxy.Tests;

public class AdSyncRunnerTests
{
    /// <summary>
    /// Fake ILdapClient that yields a canned list (or throws to simulate LDAP failure).
    /// </summary>
    private sealed class FakeLdap : ILdapClient
    {
        private readonly IReadOnlyList<AdUserDto>? _users;
        private readonly Exception? _throwOnSearch;

        public FakeLdap(IReadOnlyList<AdUserDto> users) => _users = users;
        public FakeLdap(Exception ex) => _throwOnSearch = ex;

        public Task<bool> BindAsUserAsync(string userDn, string password, CancellationToken ct) =>
            Task.FromResult(false);

        public async IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            if (_throwOnSearch is not null) throw _throwOnSearch;

            foreach (var u in _users!)
            {
                yield return u;
                await Task.Yield();
            }
        }
    }

    private static AdUserDto MakeDto(Guid guid, string username, bool enabled = true) =>
        new(
            ObjectGuid: guid,
            Username: username,
            Domain: "CORP",
            DistinguishedName: $"CN={username},OU=Users,DC=corp,DC=com",
            DisplayName: username,
            Mail: null,
            Groups: Array.Empty<string>(),
            Enabled: enabled);

    [Fact]
    public async Task FirstSync_InsertsAllUsers_AndDisablesNone()
    {
        using var ctx = new TestDbContext();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var ldap = new FakeLdap(new[] { MakeDto(alice, "alice"), MakeDto(bob, "bob") });
        var store = new UserStore(ctx.Db);
        var audit = new EfAuditLogger(new EfAuditStore(ctx.Db));
        var runner = new AdSyncRunner(ldap, store, audit, NullLogger<AdSyncRunner>.Instance);

        var result = await runner.RunOnceAsync(default);

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Disabled);
        Assert.Equal(2, await ctx.Db.Users.CountAsync());
    }

    [Fact]
    public async Task UserRemovedFromAd_IsDisabledOnNextSync()
    {
        using var ctx = new TestDbContext();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var store = new UserStore(ctx.Db);
        var audit = new EfAuditLogger(new EfAuditStore(ctx.Db));

        var ldap1 = new FakeLdap(new[] { MakeDto(alice, "alice"), MakeDto(bob, "bob") });
        await new AdSyncRunner(ldap1, store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        // Second run: bob is gone from AD.
        var ldap2 = new FakeLdap(new[] { MakeDto(alice, "alice") });
        var result = await new AdSyncRunner(ldap2, store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Disabled);
        Assert.False((await ctx.Db.Users.SingleAsync(u => u.Username == "bob")).Enabled);
        Assert.True((await ctx.Db.Users.SingleAsync(u => u.Username == "alice")).Enabled);
    }

    [Fact]
    public async Task PwdHashIsPreservedAcrossSyncs()
    {
        using var ctx = new TestDbContext();
        var alice = Guid.NewGuid();
        var store = new UserStore(ctx.Db);
        var audit = new EfAuditLogger(new EfAuditStore(ctx.Db));

        await new AdSyncRunner(new FakeLdap(new[] { MakeDto(alice, "alice") }),
                store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        // Simulate first login: PWD hash cached.
        var u = await ctx.Db.Users.SingleAsync();
        u.PwdHash = "argon2$cached";
        u.PwdHashUpdatedAt = DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync();

        // Sync again with updated display name.
        var updatedDto = MakeDto(alice, "alice") with { DisplayName = "Alice Renamed" };
        await new AdSyncRunner(new FakeLdap(new[] { updatedDto }),
                store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        var u2 = await ctx.Db.Users.SingleAsync();
        Assert.Equal("Alice Renamed", u2.DisplayName);
        Assert.Equal("argon2$cached", u2.PwdHash);
    }

    [Fact]
    public async Task LdapFailure_Propagates_AndNobodyDisabled()
    {
        using var ctx = new TestDbContext();
        var alice = Guid.NewGuid();
        var store = new UserStore(ctx.Db);
        var audit = new EfAuditLogger(new EfAuditStore(ctx.Db));

        // Seed: alice exists and is enabled.
        await new AdSyncRunner(new FakeLdap(new[] { MakeDto(alice, "alice") }),
                store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        // Failing LDAP on second run.
        var failingLdap = new FakeLdap(new InvalidOperationException("LDAP down"));
        var runner = new AdSyncRunner(failingLdap, store, audit, NullLogger<AdSyncRunner>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunOnceAsync(default));

        // alice must NOT have been disabled by a failed sync.
        Assert.True((await ctx.Db.Users.SingleAsync()).Enabled);
    }

    [Fact]
    public async Task DisabledInAd_PropagatesToLocalEnabled()
    {
        using var ctx = new TestDbContext();
        var alice = Guid.NewGuid();
        var store = new UserStore(ctx.Db);
        var audit = new EfAuditLogger(new EfAuditStore(ctx.Db));

        await new AdSyncRunner(new FakeLdap(new[] { MakeDto(alice, "alice", enabled: true) }),
                store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        await new AdSyncRunner(new FakeLdap(new[] { MakeDto(alice, "alice", enabled: false) }),
                store, audit, NullLogger<AdSyncRunner>.Instance)
            .RunOnceAsync(default);

        Assert.False((await ctx.Db.Users.SingleAsync()).Enabled);
    }
}
