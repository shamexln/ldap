using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Facades.Admin;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Tests;

public class UsersControllerTests
{
    private sealed class Fixture : IDisposable
    {
        public TestDbContext Ctx { get; } = new();
        public PasswordHasher Hasher { get; } = new();
        public EfAuditLogger Audit { get; }
        public UsersController Controller { get; }

        public Fixture()
        {
            Audit = new EfAuditLogger(Ctx.Db);
            Controller = new UsersController(new UserStore(Ctx.Db), Hasher, Audit);
        }

        public async Task<User> SeedAsync(
            string id, string username, string domain,
            bool enabled = true, string? pinPlaintext = null)
        {
            var user = new User
            {
                Id = id,
                Username = username,
                Domain = domain,
                Enabled = enabled,
                PinHash = pinPlaintext is null ? null : Hasher.Hash(pinPlaintext),
            };
            Ctx.Db.Users.Add(user);
            await Ctx.Db.SaveChangesAsync();
            return user;
        }

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public async Task List_ReturnsOrderedByDomainThenUsername()
    {
        using var f = new Fixture();
        await f.SeedAsync("1", "bob", "CORP");
        await f.SeedAsync("2", "alice", "CORP");
        await f.SeedAsync("3", "carol", "DEV");

        var result = (OkObjectResult)await f.Controller.List(null, null, 100, default);
        var items = (IReadOnlyList<UserListItemDto>)result.Value!;
        Assert.Collection(items,
            u => Assert.Equal("alice", u.Username),
            u => Assert.Equal("bob", u.Username),
            u => Assert.Equal("carol", u.Username));
    }

    [Fact]
    public async Task List_FilterByEnabled()
    {
        using var f = new Fixture();
        await f.SeedAsync("1", "a", "CORP", enabled: true);
        await f.SeedAsync("2", "b", "CORP", enabled: false);

        var result = (OkObjectResult)await f.Controller.List(null, enabled: false, 100, default);
        var items = (IReadOnlyList<UserListItemDto>)result.Value!;
        Assert.Single(items);
        Assert.Equal("b", items[0].Username);
    }

    [Fact]
    public async Task List_Search_MatchesUsernameOrDisplayName()
    {
        using var f = new Fixture();
        await f.SeedAsync("1", "alice", "CORP");
        var bob = await f.SeedAsync("2", "bob", "CORP");
        bob.DisplayName = "Bobby Tables";
        await f.Ctx.Db.SaveChangesAsync();

        var hits = (IReadOnlyList<UserListItemDto>)(
            (OkObjectResult)await f.Controller.List("Tables", null, 100, default)).Value!;
        Assert.Single(hits);
        Assert.Equal("bob", hits[0].Username);
    }

    [Fact]
    public async Task GetById_NotFound()
    {
        using var f = new Fixture();
        var result = await f.Controller.GetById("nope", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsDetailWithCards()
    {
        using var f = new Fixture();
        var u = await f.SeedAsync("1", "alice", "CORP");
        f.Ctx.Db.UserCards.Add(new UserCard
        {
            Id = "c1", UserId = u.Id, CardUidHash = "h", CardUidLast4 = "1234",
            IssuedAt = DateTime.UtcNow,
        });
        await f.Ctx.Db.SaveChangesAsync();

        var res = (OkObjectResult)await f.Controller.GetById("1", default);
        var detail = (UserDetailDto)res.Value!;
        Assert.Equal("alice", detail.Username);
        Assert.Single(detail.Cards);
        Assert.Equal("1234", detail.Cards[0].Last4);
    }

    [Fact]
    public async Task Patch_TogglesEnabled()
    {
        using var f = new Fixture();
        await f.SeedAsync("1", "a", "CORP", enabled: true);

        var result = await f.Controller.Patch("1", new PatchUserDto(false), default);
        Assert.IsType<NoContentResult>(result);
        Assert.False((await f.Ctx.Db.Users.FindAsync("1"))!.Enabled);
    }

    [Fact]
    public async Task Patch_NotFound()
    {
        using var f = new Fixture();
        var result = await f.Controller.Patch("nope", new PatchUserDto(true), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SetPin_HashesAndResetsLockout()
    {
        using var f = new Fixture();
        var u = await f.SeedAsync("1", "a", "CORP");
        u.PinFailCount = 5;
        u.PinLockedUntil = DateTime.UtcNow.AddMinutes(10);
        await f.Ctx.Db.SaveChangesAsync();

        var result = await f.Controller.SetPin("1", new SetPinDto("1234"), default);
        Assert.IsType<NoContentResult>(result);

        var reloaded = await f.Ctx.Db.Users.FindAsync("1");
        Assert.True(f.Hasher.Verify("1234", reloaded!.PinHash!));
        Assert.Equal(0, reloaded.PinFailCount);
        Assert.Null(reloaded.PinLockedUntil);
    }

    [Fact]
    public async Task SetPin_ShortPinRejected()
    {
        using var f = new Fixture();
        await f.SeedAsync("1", "a", "CORP");
        var result = await f.Controller.SetPin("1", new SetPinDto("12"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClearPin_RemovesPinHashAndLockout()
    {
        using var f = new Fixture();
        await f.SeedAsync("1", "a", "CORP", pinPlaintext: "1234");

        await f.Controller.ClearPin("1", default);

        var reloaded = await f.Ctx.Db.Users.FindAsync("1");
        Assert.Null(reloaded!.PinHash);
    }

    [Fact]
    public async Task Unlock_ClearsAllCountersAndLocks()
    {
        using var f = new Fixture();
        var u = await f.SeedAsync("1", "a", "CORP");
        u.PwdFailCount = 5;
        u.PwdLockedUntil = DateTime.UtcNow.AddHours(1);
        u.PinFailCount = 3;
        u.PinLockedUntil = DateTime.UtcNow.AddHours(1);
        await f.Ctx.Db.SaveChangesAsync();

        await f.Controller.Unlock("1", default);

        var r = await f.Ctx.Db.Users.FindAsync("1");
        Assert.Equal(0, r!.PwdFailCount);
        Assert.Null(r.PwdLockedUntil);
        Assert.Equal(0, r.PinFailCount);
        Assert.Null(r.PinLockedUntil);
    }
}
