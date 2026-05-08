using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Tokens;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

public class PwdAuthenticator : IPwdAuthenticator
{
    private readonly IUserStore _users;
    private readonly IRemotePasswordVerifier _remote;
    private readonly ILdapClient _ldap;
    private readonly IPasswordHasher _hasher;
    private readonly ILockoutPolicy _lockout;
    private readonly ITicketIssuer _tickets;
    private readonly IAuditSink _audit;
    private readonly AuthPolicyConfig _policy;
    private readonly AdConfig _adConfig;
    private readonly ILogger<PwdAuthenticator> _logger;

    public PwdAuthenticator(
        IUserStore users,
        IRemotePasswordVerifier remote,
        ILdapClient ldap,
        IPasswordHasher hasher,
        ILockoutPolicy lockout,
        ITicketIssuer tickets,
        IAuditSink audit,
        IOptions<AuthPolicyConfig> policy,
        IOptions<AdConfig> adConfig,
        ILogger<PwdAuthenticator> logger)
    {
        _users = users;
        _remote = remote;
        _ldap = ldap;
        _hasher = hasher;
        _lockout = lockout;
        _tickets = tickets;
        _audit = audit;
        _policy = policy.Value;
        _adConfig = adConfig.Value;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(
        string username, string domain, string password, CancellationToken ct)
    {
        // Input validation is the caller's responsibility; defensive check here.
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(domain) ||
            string.IsNullOrEmpty(password))
        {
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "missing credentials");
        }

        var user = await _users.FindEnabledForLoginAsync(username, domain, ct);
        if (user is null && IsOnDemandMode())
        {
            var onDemand = await _ldap.BindAndSearchSelfAsync(username, domain, password, ct);
            switch (onDemand.Outcome)
            {
                case RemoteVerifyOutcome.Valid when onDemand.User is not null:
                    await _users.UpsertFromAdAsync(onDemand.User, ct);
                    user = await _users.FindEnabledForLoginAsync(username, domain, ct);
                    if (user is null)
                    {
                        await _audit.LogAsync("pwd_login_fail",
                            username: username, domain: domain,
                            detail: new { reason = "ondemand_upsert_failed" }, ct: ct);
                        return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "invalid credentials");
                    }
                    var hash = _hasher.Hash(password);
                    await _users.UpdatePwdHashAsync(user.Id, hash, ct);
                    return await SucceedAsync(user, username, domain, "ondemand_first_login", ct);

                case RemoteVerifyOutcome.Unreachable:
                    _logger.LogError("OnDemand verifier unreachable for {Username}@{Domain}: {Diag}",
                        username, domain, onDemand.Diagnostic);
                    await _audit.LogAsync("pwd_login_error",
                        username: username, domain: domain,
                        detail: new { reason = "ondemand_unreachable", error = onDemand.Diagnostic }, ct: ct);
                    return new AuthResult.Failure(ReturnCodes.RtcSystemError, "system error");

                default:
                    await _audit.LogAsync("pwd_login_fail",
                        username: username, domain: domain,
                        detail: new { reason = "ondemand_invalid" }, ct: ct);
                    return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "invalid credentials");
            }
        }

        if (user is null)
        {
            await _audit.LogAsync("pwd_login_fail",
                username: username, domain: domain,
                detail: new { reason = "user_not_found_or_disabled" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "invalid credentials");
        }

        var lockState = await _lockout.CheckAsync(user.Id, PwdOrPin.Pwd, ct);
        if (lockState.IsLocked)
        {
            await _audit.LogAsync("pwd_login_fail",
                username: username, domain: domain,
                detail: new { reason = "locked", until = lockState.LockedUntil }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcAccountLocked, "account locked");
        }

        // Path A: local hash present, not expired, and matches → fast success.
        if (HasFreshLocalHash(user) && _hasher.Verify(password, user.PwdHash!))
        {
            return await SucceedAsync(user, username, domain, "local_hash", ct);
        }

        // Path B: fall back to the remote verifier (today: LDAP bind; future: SAML ECP / OIDC ROPC).
        //         Covers first login, TTL expiry, and the case where the user changed
        //         their password at the external identity source.
        var identity = BuildIdentity(user);
        var verify = await _remote.VerifyAsync(identity, password, ct);

        switch (verify.Outcome)
        {
            case RemoteVerifyOutcome.Valid:
                // Refresh the local hash so subsequent logins stay fast.
                var newHash = _hasher.Hash(password);
                await _users.UpdatePwdHashAsync(user.Id, newHash, ct);
                user.PwdHash = newHash;
                user.PwdHashUpdatedAt = DateTime.UtcNow;
                return await SucceedAsync(user, username, domain, "bind_fallback", ct);

            case RemoteVerifyOutcome.Invalid:
                var after = await _lockout.OnFailureAsync(user.Id, PwdOrPin.Pwd, ct);
                await _audit.LogAsync("pwd_login_fail",
                    username: username, domain: domain,
                    detail: new { reason = "remote_invalid", justLocked = after.IsLocked }, ct: ct);
                return new AuthResult.CredentialFailure(user,
                    after.IsLocked ? ReturnCodes.RtcAccountLocked : ReturnCodes.RtcInvalidCredentials,
                    "invalid credentials");

            case RemoteVerifyOutcome.Unreachable:
            default:
                // Outage: don't count as a failed attempt, don't consume lockout budget.
                // User sees "system error" (retry later) rather than "invalid credentials".
                _logger.LogError("Remote verifier unreachable for {Username}@{Domain}: {Diag}",
                    username, domain, verify.Diagnostic);
                await _audit.LogAsync("pwd_login_error",
                    username: username, domain: domain,
                    detail: new { reason = "remote_verifier_unreachable", error = verify.Diagnostic }, ct: ct);
                return new AuthResult.Failure(ReturnCodes.RtcSystemError, "system error");
        }
    }

    private async Task<AuthResult> SucceedAsync(
        User user, string username, string domain, string path, CancellationToken ct)
    {
        await _lockout.OnSuccessAsync(user.Id, PwdOrPin.Pwd, ct);
        var ticket = _tickets.Issue(user);
        await _audit.LogAsync("pwd_login_ok",
            username: username, domain: domain,
            detail: new { path }, ct: ct);
        return new AuthResult.Success(user, ticket);
    }

    private bool HasFreshLocalHash(User user)
    {
        if (string.IsNullOrEmpty(user.PwdHash)) return false;
        if (user.PwdHashUpdatedAt is null) return false;

        var age = DateTime.UtcNow - user.PwdHashUpdatedAt.Value;
        return age <= TimeSpan.FromDays(_policy.PwdHashTtlDays);
    }

    /// <summary>
    /// Build the protocol-neutral identity from our <see cref="User"/> row.
    /// Each <see cref="IRemotePasswordVerifier"/> impl picks the field it needs.
    /// </summary>
    private static UserIdentity BuildIdentity(User user) => new(
        Username: user.Username,
        Domain: user.Domain,
        DistinguishedName: user.AdDistinguishedName,
        UserPrincipalName: null,             // not stored on User today; future schema extension
        ObjectGuid: user.AdObjectGuid);

    private bool IsOnDemandMode() =>
        string.Equals(_adConfig.Mode, "OnDemand", StringComparison.OrdinalIgnoreCase);
}
