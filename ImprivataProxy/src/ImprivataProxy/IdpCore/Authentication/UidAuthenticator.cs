using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

public class UidAuthenticator : IUidAuthenticator
{
    private const string StageUidDone = "uid_done";
    private const string ModalityPin = "PIN";

    private readonly IUserStore _users;
    private readonly IAuthSessionStore _sessions;
    private readonly ITicketIssuer _tickets;
    private readonly IAuditSink _audit;
    private readonly AuthPolicyConfig _policy;
    private readonly ILogger<UidAuthenticator> _logger;

    public UidAuthenticator(
        IUserStore users,
        IAuthSessionStore sessions,
        ITicketIssuer tickets,
        IAuditSink audit,
        IOptions<AuthPolicyConfig> policy,
        ILogger<UidAuthenticator> logger)
    {
        _users = users;
        _sessions = sessions;
        _tickets = tickets;
        _audit = audit;
        _policy = policy.Value;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(string cardUid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardUid))
        {
            return new AuthResult.Failure(ReturnCodes.RtcInvalidRequest, "missing card id");
        }

        var hash = CardHasher.Hash(cardUid);
        var user = await _users.FindByCardUidHashAsync(hash, ct);
        if (user is null)
        {
            // Treat all negative outcomes as the same failure to avoid leaking which case it was.
            await _audit.LogAsync("uid_login_fail",
                detail: new { reason = "card_not_found_or_disabled" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "invalid card");
        }

        // Card-only login: user has no PIN required for this account.
        if (string.IsNullOrEmpty(user.PinHash))
        {
            var ticket = _tickets.Issue(user);
            await _audit.LogAsync("uid_login_ok",
                username: user.Username, domain: user.Domain,
                detail: new { path = "uid_only" }, ct: ct);
            return new AuthResult.Success(user, ticket);
        }

        // PIN is required: start a multi-step session.
        var serverState = await _sessions.CreateAsync(
            user.Id,
            StageUidDone,
            ModalityPin,
            TimeSpan.FromSeconds(_policy.AuthSessionTtlSeconds),
            ct);

        await _audit.LogAsync("uid_pin_challenge",
            username: user.Username, domain: user.Domain, ct: ct);

        return new AuthResult.Pending(serverState, ModalityPin);
    }
}
