using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Tests;

public class UidAuthenticatorTests
{
    private sealed class Fixture : IDisposable
    {
        public TestDbContext Ctx { get; } = new();
        public UserStore Store { get; }
        public AuthSessionStore Sessions { get; }
        public FakeTicketIssuer Tickets { get; } = new();
        public EfAuditLogger Audit { get; }
        public PasswordHasher Hasher { get; } = new();
        public UidAuthenticator Auth { get; }

        public Fixture()
        {
            Store = new UserStore(Ctx.Db);
            Sessions = new AuthSessionStore(Ctx.Db);
            Audit = new EfAuditLogger(Ctx.Db);
            Auth = new UidAuthenticator(
                Store, Sessions, Tickets, Audit,
                Options.Create(new AuthPolicyConfig { AuthSessionTtlSeconds = 60 }),
                NullLogger<UidAuthenticator>.Instance);
        }

        public async Task<User> SeedUserWithCardAsync(
            string cardUid,
            bool enabled = true,
            string? pinPlaintext = null,
            bool cardRevoked = false,
            DateTime? cardExpiresAt = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "alice",
                Domain = "CORP",
                Enabled = enabled,
                PinHash = pinPlaintext is null ? null : Hasher.Hash(pinPlaintext),
            };
            user.Cards.Add(new UserCard
            {
                Id = Guid.NewGuid().ToString(),
                CardUidHash = CardHasher.Hash(cardUid),
                CardUidLast4 = CardHasher.Last4(cardUid),
                Revoked = cardRevoked,
                ExpiresAt = cardExpiresAt,
            });
            Ctx.Db.Users.Add(user);
            await Ctx.Db.SaveChangesAsync();
            return user;
        }

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public async Task Authenticate_CardOnlyUser_DirectSuccess()
    {
        using var f = new Fixture();
        await f.SeedUserWithCardAsync("card-123");

        var result = await f.Auth.AuthenticateAsync("card-123", default);

        var ok = Assert.IsType<AuthResult.Success>(result);
        Assert.Equal("alice", ok.User.Username);
        Assert.Single(f.Tickets.Issued);
    }

    [Fact]
    public async Task Authenticate_PinRequiredUser_ReturnsPending_AndPersistsSession()
    {
        using var f = new Fixture();
        await f.SeedUserWithCardAsync("card-123", pinPlaintext: "1234");

        var result = await f.Auth.AuthenticateAsync("card-123", default);

        var pending = Assert.IsType<AuthResult.Pending>(result);
        Assert.Equal("PIN", pending.PendingModality);
        Assert.NotEmpty(pending.ServerState);

        var session = await f.Sessions.GetActiveAsync(pending.ServerState, default);
        Assert.NotNull(session);
        Assert.Equal("PIN", session.PendingModality);
        Assert.Empty(f.Tickets.Issued);
    }

    [Fact]
    public async Task Authenticate_UnknownCard_Fails()
    {
        using var f = new Fixture();
        var result = await f.Auth.AuthenticateAsync("nope", default);
        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidCredentials, fail.Rtc);
    }

    [Fact]
    public async Task Authenticate_RevokedCard_Fails()
    {
        using var f = new Fixture();
        await f.SeedUserWithCardAsync("card-123", cardRevoked: true);
        var result = await f.Auth.AuthenticateAsync("card-123", default);
        Assert.IsType<AuthResult.Failure>(result);
    }

    [Fact]
    public async Task Authenticate_ExpiredCard_Fails()
    {
        using var f = new Fixture();
        await f.SeedUserWithCardAsync("card-123", cardExpiresAt: DateTime.UtcNow.AddMinutes(-1));
        var result = await f.Auth.AuthenticateAsync("card-123", default);
        Assert.IsType<AuthResult.Failure>(result);
    }

    [Fact]
    public async Task Authenticate_DisabledUser_Fails()
    {
        using var f = new Fixture();
        await f.SeedUserWithCardAsync("card-123", enabled: false);
        var result = await f.Auth.AuthenticateAsync("card-123", default);
        Assert.IsType<AuthResult.Failure>(result);
    }

    [Fact]
    public async Task Authenticate_EmptyCardUid_RejectsBeforeLookup()
    {
        using var f = new Fixture();
        var result = await f.Auth.AuthenticateAsync("   ", default);
        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidRequest, fail.Rtc);
    }
}
