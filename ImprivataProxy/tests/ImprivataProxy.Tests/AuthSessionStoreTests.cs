using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Tests;

public class AuthSessionStoreTests
{
    [Fact]
    public async Task Create_ReturnsRandomServerState_PersistsRow()
    {
        using var ctx = new TestDbContext();
        var store = new AuthSessionStore(new EfAuthSessionRepo(ctx.Db));

        var s1 = await store.CreateAsync("u1", "uid_done", "PIN", TimeSpan.FromMinutes(1), default);
        var s2 = await store.CreateAsync("u1", "uid_done", "PIN", TimeSpan.FromMinutes(1), default);

        Assert.NotEqual(s1, s2);
        Assert.Equal(32, s1.Length);
        Assert.Equal(2, await ctx.Db.AuthSessions.CountAsync());
    }

    [Fact]
    public async Task GetActive_ReturnsSession_WithinTtl()
    {
        using var ctx = new TestDbContext();
        var store = new AuthSessionStore(new EfAuthSessionRepo(ctx.Db));
        var state = await store.CreateAsync("u1", "uid_done", "PIN", TimeSpan.FromMinutes(1), default);

        var session = await store.GetActiveAsync(state, default);

        Assert.NotNull(session);
        Assert.Equal("u1", session.UserId);
        Assert.Equal("PIN", session.PendingModality);
    }

    [Fact]
    public async Task GetActive_Expired_ReturnsNull()
    {
        using var ctx = new TestDbContext();
        var store = new AuthSessionStore(new EfAuthSessionRepo(ctx.Db));
        var state = await store.CreateAsync("u1", "uid_done", "PIN", TimeSpan.FromMilliseconds(1), default);

        await Task.Delay(30);
        var session = await store.GetActiveAsync(state, default);

        Assert.Null(session);
    }

    [Fact]
    public async Task GetActive_UnknownState_ReturnsNull()
    {
        using var ctx = new TestDbContext();
        var store = new AuthSessionStore(new EfAuthSessionRepo(ctx.Db));
        Assert.Null(await store.GetActiveAsync("nope", default));
        Assert.Null(await store.GetActiveAsync("", default));
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        using var ctx = new TestDbContext();
        var store = new AuthSessionStore(new EfAuthSessionRepo(ctx.Db));
        var state = await store.CreateAsync("u1", "uid_done", "PIN", TimeSpan.FromMinutes(1), default);

        await store.DeleteAsync(state, default);

        Assert.Null(await store.GetActiveAsync(state, default));
        Assert.Equal(0, await ctx.Db.AuthSessions.CountAsync());
    }

    [Fact]
    public async Task Create_CleansUpExpiredSessions()
    {
        using var ctx = new TestDbContext();
        var store = new AuthSessionStore(new EfAuthSessionRepo(ctx.Db));

        // Seed a long-stale session directly.
        ctx.Db.AuthSessions.Add(new Sources.Local.Entities.AuthSession
        {
            ServerState = "stale",
            UserId = "u0",
            Stage = "uid_done",
            PendingModality = "PIN",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
        });
        await ctx.Db.SaveChangesAsync();

        await store.CreateAsync("u1", "uid_done", "PIN", TimeSpan.FromMinutes(1), default);

        var remaining = await ctx.Db.AuthSessions.Select(s => s.ServerState).ToListAsync();
        Assert.DoesNotContain("stale", remaining);
        Assert.Single(remaining);
    }
}
