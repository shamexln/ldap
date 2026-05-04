using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.IdpCore.Tokens;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Contracts;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

public class PinAuthenticator : IPinAuthenticator
{
    private readonly IUserStore _users;
    private readonly IAuthSessionStore _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly ILockoutPolicy _lockout;
    private readonly ITicketIssuer _tickets;
    private readonly IAuditSink _audit;
    private readonly AuthPolicyConfig _policy;
    private readonly ILogger<PinAuthenticator> _logger;

    public PinAuthenticator(
        IUserStore users,
        IAuthSessionStore sessions,
        IPasswordHasher hasher,
        ILockoutPolicy lockout,
        ITicketIssuer tickets,
        IAuditSink audit,
        IOptions<AuthPolicyConfig> policy,
        ILogger<PinAuthenticator> logger)
    {
        _users = users;
        _sessions = sessions;
        _hasher = hasher;
        _lockout = lockout;
        _tickets = tickets;
        _audit = audit;
        _policy = policy.Value;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(string serverState, string pin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serverState))
        {
            return new AuthResult.Failure(ReturnCodes.RtcInvalidRequest, "missing server state");
        }
        if (string.IsNullOrEmpty(pin))
        {
            return new AuthResult.Failure(ReturnCodes.RtcInvalidRequest, "missing pin");
        }

        var session = await _sessions.GetActiveAsync(serverState, ct);
        if (session is null)
        {
            await _audit.LogAsync("pin_login_fail",
                detail: new { reason = "session_missing_or_expired" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcSessionExpired, "session expired");
        }

        var user = await _users.FindByIdAsync(session.UserId, ct);
        if (user is null || !user.Enabled || string.IsNullOrEmpty(user.PinHash))
        {
            // User got disabled / PIN cleared between step 1 and step 2.
            await _sessions.DeleteAsync(serverState, ct);
            await _audit.LogAsync("pin_login_fail",
                detail: new { reason = "user_disabled_or_no_pin" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "invalid");
        }

        var lockState = await _lockout.CheckAsync(user.Id, PwdOrPin.Pin, ct);
        if (lockState.IsLocked)
        {
            await _audit.LogAsync("pin_login_fail",
                username: user.Username, domain: user.Domain,
                detail: new { reason = "locked", until = lockState.LockedUntil }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcAccountLocked, "account locked");
        }

        if (!_hasher.Verify(pin, user.PinHash!))
        {
            var after = await _lockout.OnFailureAsync(user.Id, PwdOrPin.Pin, ct);
            await _audit.LogAsync("pin_login_fail",
                username: user.Username, domain: user.Domain,
                detail: new { reason = "pin_mismatch", justLocked = after.IsLocked }, ct: ct);
            // Keep the session alive so the client can retry within TTL — UX decision.
            return new AuthResult.Failure(
                after.IsLocked ? ReturnCodes.RtcAccountLocked : ReturnCodes.RtcInvalidCredentials,
                "invalid");
        }

        await _sessions.DeleteAsync(serverState, ct);
        await _lockout.OnSuccessAsync(user.Id, PwdOrPin.Pin, ct);

        var ticket = _tickets.Issue(user);
        await _audit.LogAsync("pin_login_ok",
            username: user.Username, domain: user.Domain, ct: ct);
        return new AuthResult.Success(user, ticket);
    }
}
