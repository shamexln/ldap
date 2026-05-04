using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Tests.Helpers;
using ImprivataProxy.IdpCore.Tokens;
using ImprivataProxy.Sources.Local;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Tests;

public class TicketBlacklistServiceTests
{
    [Fact]
    public async Task Add_ThenIsBlacklisted_ReturnsTrue()
    {
        using var ctx = new TestDbContext();
        var svc = new TicketBlacklistService(new EfTicketBlacklistRepo(ctx.Db));

        await svc.AddAsync("jti-1", DateTime.UtcNow.AddHours(1), default);

        Assert.True(await svc.IsBlacklistedAsync("jti-1", default));
        Assert.False(await svc.IsBlacklistedAsync("other", default));
    }

    [Fact]
    public async Task Add_IsIdempotent()
    {
        using var ctx = new TestDbContext();
        var svc = new TicketBlacklistService(new EfTicketBlacklistRepo(ctx.Db));
        var exp = DateTime.UtcNow.AddHours(1);

        await svc.AddAsync("jti-dup", exp, default);
        await svc.AddAsync("jti-dup", exp, default);
        await svc.AddAsync("jti-dup", exp, default);

        Assert.Equal(1, await ctx.Db.TicketBlacklist.CountAsync());
    }

    [Fact]
    public async Task Add_CleansUpExpiredEntries()
    {
        using var ctx = new TestDbContext();
        var svc = new TicketBlacklistService(new EfTicketBlacklistRepo(ctx.Db));

        ctx.Db.TicketBlacklist.Add(new TicketBlacklistEntry
        {
            Jti = "old",
            RevokedAt = DateTime.UtcNow.AddDays(-30),
            ExpiresAt = DateTime.UtcNow.AddDays(-29),
        });
        await ctx.Db.SaveChangesAsync();

        // Trigger opportunistic cleanup with a new add.
        await svc.AddAsync("fresh", DateTime.UtcNow.AddHours(1), default);

        var all = await ctx.Db.TicketBlacklist.ToListAsync();
        Assert.Single(all);
        Assert.Equal("fresh", all[0].Jti);
    }

    [Fact]
    public async Task IsBlacklisted_EmptyJti_ReturnsFalse()
    {
        using var ctx = new TestDbContext();
        var svc = new TicketBlacklistService(new EfTicketBlacklistRepo(ctx.Db));

        Assert.False(await svc.IsBlacklistedAsync("", default));
    }

    [Fact]
    public async Task Add_EmptyJti_NoRow()
    {
        using var ctx = new TestDbContext();
        var svc = new TicketBlacklistService(new EfTicketBlacklistRepo(ctx.Db));

        await svc.AddAsync("", DateTime.UtcNow.AddHours(1), default);

        Assert.Equal(0, await ctx.Db.TicketBlacklist.CountAsync());
    }
}
