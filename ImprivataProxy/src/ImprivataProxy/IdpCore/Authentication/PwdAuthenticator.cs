using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

public class PwdAuthenticator : IPwdAuthenticator
{
    private readonly IUserStore _users;
    private readonly ILdapClient _ldap;
    private readonly IPasswordHasher _hasher;
    private readonly ITicketIssuer _tickets;
    private readonly IAuditLogger _audit;
    private readonly AuthPolicyConfig _policy;
    private readonly ILogger<PwdAuthenticator> _logger;

    public PwdAuthenticator(
        IUserStore users,
        ILdapClient ldap,
        IPasswordHasher hasher,
        ITicketIssuer tickets,
        IAuditLogger audit,
        IOptions<AuthPolicyConfig> policy,
        ILogger<PwdAuthenticator> logger)
    {
        _users = users;
        _ldap = ldap;
        _hasher = hasher;
        _tickets = tickets;
        _audit = audit;
        _policy = policy.Value;
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
        if (user is null)
        {
            // Don't reveal whether the user exists.
            await _audit.LogAsync("pwd_login_fail",
                username: username, domain: domain,
                detail: new { reason = "user_not_found_or_disabled" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "invalid credentials");
        }

        if (IsCurrentlyLocked(user))
        {
            await _audit.LogAsync("pwd_login_fail",
                username: username, domain: domain,
                detail: new { reason = "locked", until = user.PwdLockedUntil }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcAccountLocked, "account locked");
        }

        // Path A: local hash present, not expired, and matches → fast success.
        if (HasFreshLocalHash(user) && _hasher.Verify(password, user.PwdHash!))
        {
            return await SucceedAsync(user, username, domain, "local_hash", ct);
        }

        // Path B: fall back to AD bind. Covers first login, TTL expiry, and
        //         the case where the user changed their password in AD.
        if (string.IsNullOrEmpty(user.AdDistinguishedName))
        {
            // AD-linked users always have a DN from sync; missing means misconfig or non-AD user.
            _logger.LogWarning("User {Id} has no ad_distinguished_name; cannot bind-fallback", user.Id);
            var locked = await _users.RecordPwdFailureAsync(user.Id,
                _policy.PwdMaxFails, TimeSpan.FromMinutes(_policy.PwdLockoutMinutes), ct);
            await _audit.LogAsync("pwd_login_fail",
                username: username, domain: domain,
                detail: new { reason = "no_dn_for_bind" }, ct: ct);
            return new AuthResult.Failure(
                locked ? ReturnCodes.RtcAccountLocked : ReturnCodes.RtcInvalidCredentials,
                "invalid credentials");
        }

        bool bindOk;
        try
        {
            bindOk = await _ldap.BindAsUserAsync(user.AdDistinguishedName, password, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AD bind threw for {Username}@{Domain}", username, domain);
            await _audit.LogAsync("pwd_login_error",
                username: username, domain: domain,
                detail: new { error = ex.Message }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcSystemError, "system error");
        }

        if (!bindOk)
        {
            var locked = await _users.RecordPwdFailureAsync(user.Id,
                _policy.PwdMaxFails, TimeSpan.FromMinutes(_policy.PwdLockoutMinutes), ct);
            await _audit.LogAsync("pwd_login_fail",
                username: username, domain: domain,
                detail: new { reason = "bind_failed", justLocked = locked }, ct: ct);
            return new AuthResult.Failure(
                locked ? ReturnCodes.RtcAccountLocked : ReturnCodes.RtcInvalidCredentials,
                "invalid credentials");
        }

        // Bind succeeded: refresh the local hash so subsequent logins stay fast.
        var newHash = _hasher.Hash(password);
        await _users.UpdatePwdHashAsync(user.Id, newHash, ct);

        // Reload to see the fresh hash fields; minor, but keeps audit consistent.
        user.PwdHash = newHash;
        user.PwdHashUpdatedAt = DateTime.UtcNow;

        return await SucceedAsync(user, username, domain, "bind_fallback", ct);
    }

    private async Task<AuthResult> SucceedAsync(
        User user, string username, string domain, string path, CancellationToken ct)
    {
        await _users.RecordPwdSuccessAsync(user.Id, ct);
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

    private static bool IsCurrentlyLocked(User user) =>
        user.PwdLockedUntil is not null && user.PwdLockedUntil > DateTime.UtcNow;
}
