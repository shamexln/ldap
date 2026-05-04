using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Tests;

public class PinAuthenticatorTests
{
    private sealed class Fixture : IDisposable
    {
        public TestDbContext Ctx { get; } = new();
        public UserStore Store { get; }
        public AuthSessionStore Sessions { get; }
        public PasswordHasher Hasher { get; } = new();
        public FakeTicketIssuer Tickets { get; } = new();
        public AuditLogSink Audit { get; }
        public AuthPolicyConfig Policy { get; }
        public PinAuthenticator Auth { get; }

        public Fixture(AuthPolicyConfig? policy = null)
        {
            Store = new UserStore(Ctx.Db);
            Sessions = new AuthSessionStore(new EfAuthSessionRepo(Ctx.Db));
            Audit = new AuditLogSink(new EfAuditStore(Ctx.Db));
            Policy = policy ?? new AuthPolicyConfig
            {
                PinMaxFails = 3,
                PinLockoutMinutes = 15,
                AuthSessionTtlSeconds = 60,
            };
            Auth = new PinAuthenticator(
                Store, Sessions, Hasher, Tickets, Audit,
                Options.Create(Policy),
                NullLogger<PinAuthenticator>.Instance);
        }

        public async Task<(User user, string serverState)> SeedUserWithSessionAsync(
            string pinPlaintext = "1234",
            bool enabled = true)
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "alice",
                Domain = "CORP",
                Enabled = enabled,
                PinHash = Hasher.Hash(pinPlaintext),
            };
            Ctx.Db.Users.Add(user);
            await Ctx.Db.SaveChangesAsync();

            var serverState = await Sessions.CreateAsync(
                user.Id, "uid_done", "PIN",
                TimeSpan.FromSeconds(Policy.AuthSessionTtlSeconds), default);
            return (user, serverState);
        }

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public async Task CorrectPin_ReturnsSuccess_DeletesSession_ResetsFails()
    {
        using var f = new Fixture();
        var (user, state) = await f.SeedUserWithSessionAsync();
        user.PinFailCount = 2;   // we want to confirm reset
        await f.Ctx.Db.SaveChangesAsync();

        var result = await f.Auth.AuthenticateAsync(state, "1234", default);

        Assert.IsType<AuthResult.Success>(result);
        Assert.Null(await f.Sessions.GetActiveAsync(state, default));
        Assert.Equal(0, f.Ctx.Db.Users.Single().PinFailCount);
        Assert.Single(f.Tickets.Issued);
    }

    [Fact]
    public async Task WrongPin_IncrementsFailCount_KeepsSession()
    {
        using var f = new Fixture();
        var (_, state) = await f.SeedUserWithSessionAsync();

        var result = await f.Auth.AuthenticateAsync(state, "9999", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidCredentials, fail.Rtc);
        Assert.Equal(1, f.Ctx.Db.Users.Single().PinFailCount);
        Assert.NotNull(await f.Sessions.GetActiveAsync(state, default));
    }

    [Fact]
    public async Task WrongPinThreeTimes_LocksAccount()
    {
        using var f = new Fixture(new AuthPolicyConfig
        {
            PinMaxFails = 2,
            PinLockoutMinutes = 15,
            AuthSessionTtlSeconds = 60,
        });
        var (_, state) = await f.SeedUserWithSessionAsync();

        await f.Auth.AuthenticateAsync(state, "9999", default);
        var result = await f.Auth.AuthenticateAsync(state, "9999", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcAccountLocked, fail.Rtc);
        var u = f.Ctx.Db.Users.Single();
        Assert.NotNull(u.PinLockedUntil);
        Assert.True(u.PinLockedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task LockedAccount_EvenCorrectPin_IsRejected()
    {
        using var f = new Fixture();
        var (user, state) = await f.SeedUserWithSessionAsync();
        user.PinLockedUntil = DateTime.UtcNow.AddMinutes(5);
        await f.Ctx.Db.SaveChangesAsync();

        var result = await f.Auth.AuthenticateAsync(state, "1234", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcAccountLocked, fail.Rtc);
    }

    [Fact]
    public async Task UnknownServerState_ReturnsSessionExpired()
    {
        using var f = new Fixture();
        var result = await f.Auth.AuthenticateAsync("no-such-state", "1234", default);
        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcSessionExpired, fail.Rtc);
    }

    [Fact]
    public async Task ExpiredSession_ReturnsSessionExpired()
    {
        using var f = new Fixture();
        var (user, _) = await f.SeedUserWithSessionAsync();

        // Manually craft an expired session row.
        f.Ctx.Db.AuthSessions.Add(new AuthSession
        {
            ServerState = "expired-state",
            UserId = user.Id,
            Stage = "uid_done",
            PendingModality = "PIN",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-4),
        });
        await f.Ctx.Db.SaveChangesAsync();

        var result = await f.Auth.AuthenticateAsync("expired-state", "1234", default);
        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcSessionExpired, fail.Rtc);
    }

    [Fact]
    public async Task EmptyPin_FailsBeforeSessionLookup()
    {
        using var f = new Fixture();
        var (_, state) = await f.SeedUserWithSessionAsync();

        var result = await f.Auth.AuthenticateAsync(state, "", default);

        var fail = Assert.IsType<AuthResult.Failure>(result);
        Assert.Equal(ReturnCodes.RtcInvalidRequest, fail.Rtc);
    }

    [Fact]
    public async Task UserDisabledBetweenSteps_FailsAndDeletesSession()
    {
        using var f = new Fixture();
        var (user, state) = await f.SeedUserWithSessionAsync();
        user.Enabled = false;
        await f.Ctx.Db.SaveChangesAsync();

        var result = await f.Auth.AuthenticateAsync(state, "1234", default);

        Assert.IsType<AuthResult.Failure>(result);
        Assert.Null(await f.Sessions.GetActiveAsync(state, default));
    }
}
