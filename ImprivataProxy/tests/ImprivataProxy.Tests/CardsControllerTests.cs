using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Facades.Admin;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Tests;

public class CardsControllerTests
{
    private sealed class Fixture : IDisposable
    {
        public TestDbContext Ctx { get; } = new();
        public EfAuditLogger Audit { get; }
        public CardsController Controller { get; }

        public Fixture()
        {
            Audit = new EfAuditLogger(Ctx.Db);
            Controller = new CardsController(new UserStore(Ctx.Db), Audit);
        }

        public async Task<User> SeedUserAsync(string id = "u1")
        {
            var u = new User { Id = id, Username = "alice", Domain = "CORP", Enabled = true };
            Ctx.Db.Users.Add(u);
            await Ctx.Db.SaveChangesAsync();
            return u;
        }

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public async Task Issue_CreatesCard_ReturnsCreated()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();

        var result = await f.Controller.Issue(
            new IssueCardDto("u1", "card-1234567890", "main", null),
            default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<CardDto>(created.Value);
        Assert.Equal("7890", dto.Last4);
        Assert.Equal("main", dto.Label);
        Assert.False(dto.Revoked);

        Assert.Single(f.Ctx.Db.UserCards);
    }

    [Fact]
    public async Task Issue_HashesCardUid_DoesNotStorePlaintext()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();

        await f.Controller.Issue(new IssueCardDto("u1", "card-1234567890", null, null), default);

        var card = await f.Ctx.Db.UserCards.SingleAsync();
        Assert.Equal(CardHasher.Hash("card-1234567890"), card.CardUidHash);
        // Make sure plaintext didn't sneak into any stored field.
        Assert.DoesNotContain("card-1234567890", card.CardUidHash);
        Assert.NotEqual("card-1234567890", card.Label);
    }

    [Fact]
    public async Task Issue_UnknownUser_Returns404()
    {
        using var f = new Fixture();
        var result = await f.Controller.Issue(new IssueCardDto("ghost", "c1", null, null), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Issue_DuplicateCard_Returns409()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();
        await f.Controller.Issue(new IssueCardDto("u1", "card-abc", null, null), default);

        var result = await f.Controller.Issue(new IssueCardDto("u1", "card-abc", null, null), default);
        Assert.IsType<ConflictObjectResult>(result);
        Assert.Single(f.Ctx.Db.UserCards);
    }

    [Fact]
    public async Task Issue_MissingFields_Returns400()
    {
        using var f = new Fixture();
        Assert.IsType<BadRequestObjectResult>(
            await f.Controller.Issue(new IssueCardDto("", "c", null, null), default));
        Assert.IsType<BadRequestObjectResult>(
            await f.Controller.Issue(new IssueCardDto("u1", "", null, null), default));
    }

    [Fact]
    public async Task Get_Found()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();
        var created = (CreatedAtActionResult)await f.Controller.Issue(
            new IssueCardDto("u1", "card-abc", null, null), default);
        var id = ((CardDto)created.Value!).Id;

        var result = (OkObjectResult)await f.Controller.Get(id, default);
        Assert.IsType<CardDto>(result.Value);
    }

    [Fact]
    public async Task Get_NotFound()
    {
        using var f = new Fixture();
        Assert.IsType<NotFoundResult>(await f.Controller.Get("missing", default));
    }

    [Fact]
    public async Task Revoke_SetsRevokedFlag()
    {
        using var f = new Fixture();
        await f.SeedUserAsync();
        var created = (CreatedAtActionResult)await f.Controller.Issue(
            new IssueCardDto("u1", "card-abc", null, null), default);
        var id = ((CardDto)created.Value!).Id;

        var result = await f.Controller.Revoke(id, default);
        Assert.IsType<NoContentResult>(result);

        Assert.True((await f.Ctx.Db.UserCards.FindAsync(id))!.Revoked);
    }

    [Fact]
    public async Task Revoke_NotFound()
    {
        using var f = new Fixture();
        Assert.IsType<NotFoundResult>(await f.Controller.Revoke("missing", default));
    }
}
