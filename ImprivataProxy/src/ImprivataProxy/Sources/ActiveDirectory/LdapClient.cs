using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.CompilerServices;
using ImprivataProxy.Configuration;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Contracts;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Sources.ActiveDirectory;

public class LdapClient : ILdapClient, IRemotePasswordVerifier, IOnDemandLoginProvider, IBadgeSearchProvider
{
    // LDAP result code 49 = invalidCredentials per RFC 4511.
    private const int LdapInvalidCredentials = 49;

    private static readonly string[] SyncAttributes =
    {
        "objectGUID", "sAMAccountName", "userPrincipalName",
        "distinguishedName", "displayName", "givenName", "sn",
        "mail", "memberOf", "userAccountControl"
    };

    private readonly AdConfig _config;
    private readonly ILogger<LdapClient> _logger;

    public LdapClient(IOptions<AdConfig> config, ILogger<LdapClient> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// ADR-0002 §4.1: verifies the given user's password via LDAP simple bind.
    /// Reads <see cref="UserIdentity.DistinguishedName"/>; other identity fields
    /// are ignored here (UPN / GUID would be used by SAML / OIDC implementations).
    /// Distinguishes three outcomes: Valid, Invalid(LDAP 49), Unreachable(other).
    /// </summary>
    public Task<RemoteVerifyResult> VerifyAsync(
        UserIdentity identity, string password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(identity.DistinguishedName))
        {
            _logger.LogInformation("VerifyAsync: missing DistinguishedName, returning Invalid");
            return Task.FromResult(new RemoteVerifyResult(
                RemoteVerifyOutcome.Invalid, "missing DistinguishedName"));
        }

        return Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("VerifyAsync: binding DN={Dn}, PwdLen={Len}, LdapUrl={Url}",
                    identity.DistinguishedName, password?.Length, _config.LdapUrl);
                using var conn = OpenConnection(_config.BindTimeoutSeconds);
                conn.Bind(new NetworkCredential(identity.DistinguishedName, password));
                _logger.LogInformation("VerifyAsync: bind succeeded for {Dn}", identity.DistinguishedName);
                return new RemoteVerifyResult(RemoteVerifyOutcome.Valid);
            }
            catch (LdapException ex) when (ex.ErrorCode == LdapInvalidCredentials)
            {
                _logger.LogWarning("VerifyAsync: invalid credentials for {Dn}, ErrorCode={Code}",
                    identity.DistinguishedName, ex.ErrorCode);
                return new RemoteVerifyResult(RemoteVerifyOutcome.Invalid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VerifyAsync: LDAP bind failed for {Dn}", identity.DistinguishedName);
                return new RemoteVerifyResult(RemoteVerifyOutcome.Unreachable, ex.Message);
            }
        }, ct);
    }

    public async IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var svcPwd = Environment.GetEnvironmentVariable(_config.ServiceAccountPasswordEnvVar);
        _logger.LogInformation("SearchAllUsersAsync: EnvVar={EnvVar}, PwdLen={Len}, DN={Dn}, LdapUrl={Url}, BaseDn={BaseDn}",
            _config.ServiceAccountPasswordEnvVar, svcPwd?.Length, _config.ServiceAccountDn, _config.LdapUrl, _config.BaseDn);
        if (string.IsNullOrEmpty(svcPwd))
        {
            throw new InvalidOperationException(
                $"Service account password env var '{_config.ServiceAccountPasswordEnvVar}' not set");
        }

        using var conn = OpenConnection(_config.SearchTimeoutSeconds);
        _logger.LogInformation("SearchAllUsersAsync: connection opened, attempting bind...");
        conn.Bind(new NetworkCredential(_config.ServiceAccountDn, svcPwd));
        _logger.LogInformation("SearchAllUsersAsync: bind succeeded");

        var pageControl = new PageResultRequestControl(_config.PageSize);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var request = new SearchRequest(
                _config.BaseDn,
                "(&(objectCategory=person)(objectClass=user))",
                SearchScope.Subtree,
                SyncAttributes);
            request.Controls.Add(pageControl);

            var response = (SearchResponse)conn.SendRequest(request);

            foreach (SearchResultEntry entry in response.Entries)
            {
                var dto = TryMapEntry(entry);
                if (dto is not null)
                {
                    yield return dto;
                }
            }

            var cookieControl = response.Controls
                .OfType<PageResultResponseControl>()
                .FirstOrDefault();

            if (cookieControl is null || cookieControl.Cookie.Length == 0)
            {
                break;
            }

            pageControl.Cookie = cookieControl.Cookie;
            await Task.Yield();
        }
    }

    public Task<OnDemandLoginResult> BindAndSearchSelfAsync(
        string username, string domain, string password, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                using var conn = OpenConnection(_config.BindTimeoutSeconds);
                var upn = $"{username}@{domain}";
                _logger.LogInformation("OnDemand: binding UPN={Upn}, PwdLen={Len}, LdapUrl={Url}",
                    upn, password?.Length, _config.LdapUrl);
                conn.Bind(new NetworkCredential(upn, password));
                _logger.LogInformation("OnDemand: bind succeeded for {Upn}", upn);

                var filter = $"(&(objectClass=user)(sAMAccountName={EscapeLdapFilter(username)}))";
                _logger.LogInformation("OnDemand: searching BaseDn={BaseDn}, Filter={Filter}", _config.BaseDn, filter);
                var request = new SearchRequest(
                    _config.BaseDn,
                    filter,
                    SearchScope.Subtree,
                    SyncAttributes);

                var response = (SearchResponse)conn.SendRequest(request);
                _logger.LogInformation("OnDemand: search returned {Count} entries", response.Entries.Count);
                if (response.Entries.Count == 0)
                {
                    _logger.LogWarning("OnDemand bind succeeded but user entry not found: {Upn}", upn);
                    return new OnDemandLoginResult(RemoteVerifyOutcome.Invalid, Diagnostic: "entry not found after bind");
                }

                var dto = TryMapEntry(response.Entries[0]);
                if (dto is null)
                {
                    _logger.LogWarning("OnDemand: TryMapEntry returned null for {Upn}", upn);
                    return new OnDemandLoginResult(RemoteVerifyOutcome.Invalid, Diagnostic: "failed to map entry");
                }

                _logger.LogInformation("OnDemand: success for {Upn}, mapped user={User}", upn, dto.Username);
                return new OnDemandLoginResult(RemoteVerifyOutcome.Valid, dto);
            }
            catch (LdapException ex) when (ex.ErrorCode == LdapInvalidCredentials)
            {
                _logger.LogWarning("OnDemand: invalid credentials for {Username}@{Domain}, ErrorCode={Code}",
                    username, domain, ex.ErrorCode);
                return new OnDemandLoginResult(RemoteVerifyOutcome.Invalid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnDemand: bind failed for {Username}@{Domain}", username, domain);
                return new OnDemandLoginResult(RemoteVerifyOutcome.Unreachable, Diagnostic: ex.Message);
            }
        }, ct);
    }

    public Task<AdUserDto?> SearchByBadgeAsync(string badgeValue, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var svcPwd = Environment.GetEnvironmentVariable(_config.ServiceAccountPasswordEnvVar);
            if (string.IsNullOrEmpty(svcPwd))
            {
                throw new InvalidOperationException(
                    $"Service account password env var '{_config.ServiceAccountPasswordEnvVar}' not set");
            }

            _logger.LogInformation("SearchByBadge: DN={Dn}, PwdLen={Len}, LdapUrl={Url}, Badge={Badge}",
                _config.ServiceAccountDn, svcPwd?.Length, _config.LdapUrl, badgeValue);
            using var conn = OpenConnection(_config.SearchTimeoutSeconds);
            _logger.LogInformation("SearchByBadge: connection opened, attempting bind...");
            conn.Bind(new NetworkCredential(_config.ServiceAccountDn, svcPwd));
            _logger.LogInformation("SearchByBadge: bind succeeded");

            var filter = $"(&(objectCategory=person)(objectClass=user)({EscapeLdapFilter(_config.BadgeAttribute)}={EscapeLdapFilter(badgeValue)}))";
            _logger.LogInformation("SearchByBadge: filter={Filter}, BaseDn={BaseDn}", filter, _config.BaseDn);
            var request = new SearchRequest(
                _config.BaseDn,
                filter,
                SearchScope.Subtree,
                SyncAttributes);

            var response = (SearchResponse)conn.SendRequest(request);
            _logger.LogInformation("SearchByBadge: search returned {Count} entries", response.Entries.Count);
            if (response.Entries.Count == 0)
            {
                _logger.LogWarning("SearchByBadge: no results for {Attribute}={Value}",
                    _config.BadgeAttribute, badgeValue);
                return null;
            }

            var dto = TryMapEntry(response.Entries[0]);
            _logger.LogInformation("SearchByBadge: mapped result={User}", dto?.Username ?? "(null)");
            return dto;
        }, ct);
    }

    private static string EscapeLdapFilter(string input)
    {
        return input
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }

    private LdapConnection OpenConnection(int timeoutSeconds)
    {
        var uri = new Uri(_config.LdapUrl);
        _logger.LogInformation("OpenConnection: Host={Host}, Port={Port}, Scheme={Scheme}, Timeout={Timeout}s, SkipCert={SkipCert}",
            uri.Host, uri.Port, uri.Scheme, timeoutSeconds, _config.SkipCertValidation);
        var identifier = new LdapDirectoryIdentifier(uri.Host, uri.Port);
        var conn = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        conn.SessionOptions.ProtocolVersion = 3;

        if (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.SkipCertValidation)
            {
                conn.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }
            conn.SessionOptions.SecureSocketLayer = true;
            _logger.LogInformation("OpenConnection: SSL enabled");
        }

        return conn;
    }

    private AdUserDto? TryMapEntry(SearchResultEntry entry)
    {
        try
        {
            var guidBytes = GetBinaryValue(entry, "objectGUID");
            var samName = GetStringValue(entry, "sAMAccountName");
            if (guidBytes is null || string.IsNullOrEmpty(samName))
            {
                _logger.LogWarning("Skipping AD entry missing objectGUID or sAMAccountName: {Dn}",
                    entry.DistinguishedName);
                return null;
            }

            var dn = entry.DistinguishedName ?? GetStringValue(entry, "distinguishedName") ?? "";
            var upn = GetStringValue(entry, "userPrincipalName");
            var domain = ExtractDomain(upn, dn);

            var displayName = GetStringValue(entry, "displayName");
            var givenName = GetStringValue(entry, "givenName");
            var sn = GetStringValue(entry, "sn");
            var mail = GetStringValue(entry, "mail");

            var uacStr = GetStringValue(entry, "userAccountControl");
            var uac = int.TryParse(uacStr, out var u) ? u : UacFlags.NORMAL_ACCOUNT;
            var enabled = UacFlags.IsEnabled(uac);

            var groups = GetStringValues(entry, "memberOf")
                .Select(DnParser.ExtractLeftmostCn)
                .Where(cn => !string.IsNullOrEmpty(cn))
                .Select(cn => cn!)
                .ToList();

            return new AdUserDto(
                ObjectGuid: new Guid(guidBytes),
                Username: samName,
                Domain: domain,
                DistinguishedName: dn,
                DisplayName: displayName,
                GivenName: givenName,
                Sn: sn,
                Mail: mail,
                Groups: groups,
                Enabled: enabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to map AD entry {Dn}", entry.DistinguishedName);
            return null;
        }
    }

    private static string ExtractDomain(string? upn, string dn)
    {
        if (!string.IsNullOrEmpty(upn))
        {
            var at = upn.IndexOf('@');
            if (at > 0 && at < upn.Length - 1)
            {
                return upn[(at + 1)..];
            }
        }

        return DnParser.ExtractDomainFromDn(dn) ?? "";
    }

    private static string? GetStringValue(SearchResultEntry entry, string name)
    {
        var attr = entry.Attributes[name];
        if (attr is null || attr.Count == 0) return null;
        var values = attr.GetValues(typeof(string));
        if (values.Length == 0) return null;
        return values[0] as string;
    }

    private static IEnumerable<string> GetStringValues(SearchResultEntry entry, string name)
    {
        var attr = entry.Attributes[name];
        if (attr is null || attr.Count == 0) yield break;
        foreach (var v in attr.GetValues(typeof(string)))
        {
            if (v is string s && !string.IsNullOrEmpty(s)) yield return s;
        }
    }

    private static byte[]? GetBinaryValue(SearchResultEntry entry, string name)
    {
        var attr = entry.Attributes[name];
        if (attr is null || attr.Count == 0) return null;
        var values = attr.GetValues(typeof(byte[]));
        if (values.Length == 0) return null;
        return values[0] as byte[];
    }
}
