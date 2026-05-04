using ImprivataProxy.Sources.Local;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

public class PinAuthenticator : IPinAuthenticator
{
    private readonly IUserStore _users;
    private readonly IAuthSessionStore _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly ITicketIssuer _tickets;
    private readonly IAuditLogger _audit;
    private readonly AuthPolicyConfig _policy;
    private readonly ILogger<PinAuthenticator> _logger;

    public PinAuthenticator(
        IUserStore users,
        IAuthSessionStore sessions,
        IPasswordHasher hasher,
        ITicketIssuer tickets,
        IAuditLogger audit,
        IOptions<AuthPolicyConfig> policy,
        ILogger<PinAuthenticator> logger)
    {
        _users = users;
        _sessions = sessions;
        _hasher = hasher;
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

        if (IsCurrentlyLocked(user))
        {
            await _audit.LogAsync("pin_login_fail",
                username: user.Username, domain: user.Domain,
                detail: new { reason = "locked", until = user.PinLockedUntil }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcAccountLocked, "account locked");
        }

        if (!_hasher.Verify(pin, user.PinHash!))
        {
            var locked = await _users.RecordPinFailureAsync(
                user.Id,
                _policy.PinMaxFails,
                TimeSpan.FromMinutes(_policy.PinLockoutMinutes),
                ct);
            await _audit.LogAsync("pin_login_fail",
                username: user.Username, domain: user.Domain,
                detail: new { reason = "pin_mismatch", justLocked = locked }, ct: ct);
            // Keep the session alive so the client can retry within TTL — UX decision.
            return new AuthResult.Failure(
                locked ? ReturnCodes.RtcAccountLocked : ReturnCodes.RtcInvalidCredentials,
                "invalid");
        }

        await _sessions.DeleteAsync(serverState, ct);
        await _users.RecordPinSuccessAsync(user.Id, ct);

        var ticket = _tickets.Issue(user);
        await _audit.LogAsync("pin_login_ok",
            username: user.Username, domain: user.Domain, ct: ct);
        return new AuthResult.Success(user, ticket);
    }

    private static bool IsCurrentlyLocked(User user) =>
        user.PinLockedUntil is not null && user.PinLockedUntil > DateTime.UtcNow;
}
