using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Authorization;
using ImprivataProxy.IdpCore.Tokens;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Sources.Contracts;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.IdpCore.Authentication;

public class BadgeUidAuthenticator : IUidAuthenticator
{
    private readonly ILdapClient _ldap;
    private readonly IUserStore _users;
    private readonly ITicketIssuer _tickets;
    private readonly IAuditSink _audit;
    private readonly GroupAuthorizationChecker _groupChecker;
    private readonly AdConfig _config;
    private readonly ILogger<BadgeUidAuthenticator> _logger;

    public BadgeUidAuthenticator(
        ILdapClient ldap,
        IUserStore users,
        ITicketIssuer tickets,
        IAuditSink audit,
        GroupAuthorizationChecker groupChecker,
        IOptions<AdConfig> config,
        ILogger<BadgeUidAuthenticator> logger)
    {
        _ldap = ldap;
        _users = users;
        _tickets = tickets;
        _audit = audit;
        _groupChecker = groupChecker;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(string cardUid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardUid))
        {
            return new AuthResult.Failure(ReturnCodes.RtcInvalidRequest, "missing badge value");
        }

        AdUserDto? dto;
        try
        {
            dto = await _ldap.SearchByBadgeAsync(cardUid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Badge AD search failed for value {Badge}", cardUid);
            return new AuthResult.Failure(ReturnCodes.RtcSystemError, "AD unreachable");
        }

        if (dto is null)
        {
            await _audit.LogAsync("badge_login_fail",
                detail: new { reason = "badge_not_found", badge = cardUid }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "badge not found");
        }

        if (!dto.Enabled)
        {
            await _audit.LogAsync("badge_login_fail",
                username: dto.Username, domain: dto.Domain,
                detail: new { reason = "account_disabled" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "account disabled");
        }

        if (!_groupChecker.IsAuthorized(dto.Groups, _config.RequiredGroups))
        {
            await _audit.LogAsync("badge_login_fail",
                username: dto.Username, domain: dto.Domain,
                detail: new { reason = "not_in_required_groups" }, ct: ct);
            return new AuthResult.Failure(ReturnCodes.RtcInvalidCredentials, "unauthorized group");
        }

        await _users.UpsertFromAdAsync(dto, ct);
        var user = await _users.FindEnabledForLoginAsync(dto.Username, dto.Domain, ct);
        if (user is null)
        {
            return new AuthResult.Failure(ReturnCodes.RtcSystemError, "upsert failed");
        }

        var ticket = _tickets.Issue(user);
        await _audit.LogAsync("badge_login_ok",
            username: user.Username, domain: user.Domain,
            detail: new { path = "badge_ad" }, ct: ct);

        return new AuthResult.Success(user, ticket);
    }
}
