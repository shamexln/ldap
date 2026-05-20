using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Tests;

public class UserStoreTests
{
    private static AdUserDto MakeDto(
        Guid? guid = null,
        string username = "alice",
        string domain = "CORP",
        string dn = "CN=alice,OU=Users,DC=corp,DC=example,DC=com",
        string? display = "Alice Smith",
        string? givenName = null,
        string? sn = null,
        string? mail = "alice@corp.example.com",
        string[]? groups = null,
        bool enabled = true) =>
        new(
            ObjectGuid: guid ?? Guid.NewGuid(),
            Username: username,
            Domain: domain,
            DistinguishedName: dn,
            DisplayName: display,
            GivenName: givenName,
            Sn: sn,
            Mail: mail,
            Groups: groups ?? new[] { "Domain Users" },
            Enabled: enabled);

    [Fact]
    public async Task UpsertFromAd_NewUser_Inserts()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);
        var dto = MakeDto();

        var outcome = await store.UpsertFromAdAsync(dto, default);

        Assert.Equal(UpsertOutcome.Inserted, outcome);
        var user = await ctx.Db.Users.SingleAsync();
        Assert.Equal("alice", user.Username);
        Assert.Equal(dto.ObjectGuid.ToString(), user.AdObjectGuid);
        Assert.Null(user.PinHash);
        Assert.True(user.Enabled);
        Assert.NotNull(user.LastSyncedAt);
    }

    [Fact]
    public async Task UpsertFromAd_ExistingGuid_Updates_WithoutTouchingPinHash()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);
        var guid = Guid.NewGuid();

        await store.UpsertFromAdAsync(MakeDto(guid, display: "Old Name"), default);

        // Admin sets PIN.
        var u = await ctx.Db.Users.SingleAsync();
        u.PinHash = "argon2$pin$frozen";
        await ctx.Db.SaveChangesAsync();

        // Next AD sync brings updated display name.
        var outcome = await store.UpsertFromAdAsync(MakeDto(guid, display: "New Name"), default);

        Assert.Equal(UpsertOutcome.Updated, outcome);
        var updated = await ctx.Db.Users.SingleAsync();
        Assert.Equal("New Name", updated.DisplayName);
        Assert.Equal("argon2$pin$frozen", updated.PinHash);
    }

    [Fact]
    public async Task UpsertFromAd_IdenticalSecondSync_ReturnsUnchanged()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);
        var dto = MakeDto();

        await store.UpsertFromAdAsync(dto, default);
        var outcome = await store.UpsertFromAdAsync(dto, default);

        Assert.Equal(UpsertOutcome.Unchanged, outcome);
    }

    [Fact]
    public async Task UpsertFromAd_DisabledInAd_SetsEnabledFalse()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);
        var guid = Guid.NewGuid();

        await store.UpsertFromAdAsync(MakeDto(guid, enabled: true), default);
        await store.UpsertFromAdAsync(MakeDto(guid, enabled: false), default);

        var u = await ctx.Db.Users.SingleAsync();
        Assert.False(u.Enabled);
    }

    [Fact]
    public async Task DisableUsersNotIn_DisablesStaleAdLinkedOnly()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await store.UpsertFromAdAsync(MakeDto(alice, username: "alice"), default);
        await store.UpsertFromAdAsync(MakeDto(bob, username: "bob"), default);

        // Simulate a purely local account (no AD linkage).
        ctx.Db.Users.Add(new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "local-only",
            Domain = "LOCAL",
            AdObjectGuid = null,
            Enabled = true,
        });
        await ctx.Db.SaveChangesAsync();

        // Next sync only sees alice.
        var disabled = await store.DisableUsersNotInAsync(
            new HashSet<string> { alice.ToString() }, default);

        Assert.Equal(1, disabled);
        Assert.True((await ctx.Db.Users.SingleAsync(u => u.Username == "alice")).Enabled);
        Assert.False((await ctx.Db.Users.SingleAsync(u => u.Username == "bob")).Enabled);
        // Purely local account must not be touched.
        Assert.True((await ctx.Db.Users.SingleAsync(u => u.Username == "local-only")).Enabled);
    }

    [Fact]
    public async Task DisableUsersNotIn_EmptySet_DisablesAllAdLinkedEnabled()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);
        await store.UpsertFromAdAsync(MakeDto(username: "a"), default);
        await store.UpsertFromAdAsync(MakeDto(username: "b"), default);

        var disabled = await store.DisableUsersNotInAsync(new HashSet<string>(), default);

        Assert.Equal(2, disabled);
        Assert.All(await ctx.Db.Users.ToListAsync(), u => Assert.False(u.Enabled));
    }

    [Fact]
    public async Task DisableUsersNotIn_AlreadyDisabled_NotCountedAgain()
    {
        using var ctx = new TestDbContext();
        var store = new UserStore(ctx.Db);
        var guid = Guid.NewGuid();
        await store.UpsertFromAdAsync(MakeDto(guid, enabled: false), default);

        var disabled = await store.DisableUsersNotInAsync(new HashSet<string>(), default);

        Assert.Equal(0, disabled);
    }
}
