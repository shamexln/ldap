using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.CompilerServices;
using ImprivataProxy.Configuration;
using ImprivataProxy.Sources.Contracts;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Sources.ActiveDirectory;

public class LdapClient : ILdapClient, IRemotePasswordVerifier
{
    /// <summary>
    /// ADR-0002 §4.1 适配:把 LDAP simple bind 包装成 IRemotePasswordVerifier 契约。
    /// 行为等价于 BindAsUserAsync,但返回三态结果(Valid/Invalid/Unreachable)。
    /// 当前 PwdAuthenticator 仍调 BindAsUserAsync;IRemotePasswordVerifier 作为未来切换点就位。
    /// </summary>
    public async Task<RemoteVerifyResult> VerifyAsync(
        string distinguishedName, string password, CancellationToken ct)
    {
        try
        {
            var ok = await BindAsUserAsync(distinguishedName, password, ct);
            return new RemoteVerifyResult(
                ok ? RemoteVerifyOutcome.Valid : RemoteVerifyOutcome.Invalid);
        }
        catch (Exception ex)
        {
            return new RemoteVerifyResult(RemoteVerifyOutcome.Unreachable, ex.Message);
        }
    }


    // LDAP result code 49 = invalidCredentials per RFC 4511.
    private const int LdapInvalidCredentials = 49;

    private static readonly string[] SyncAttributes =
    {
        "objectGUID", "sAMAccountName", "userPrincipalName",
        "distinguishedName", "displayName", "mail",
        "memberOf", "userAccountControl"
    };

    private readonly AdConfig _config;
    private readonly ILogger<LdapClient> _logger;

    public LdapClient(IOptions<AdConfig> config, ILogger<LdapClient> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public Task<bool> BindAsUserAsync(string userDn, string password, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                using var conn = OpenConnection(_config.BindTimeoutSeconds);
                conn.Bind(new NetworkCredential(userDn, password));
                return true;
            }
            catch (LdapException ex) when (ex.ErrorCode == LdapInvalidCredentials)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User bind failed for {UserDn}", userDn);
                return false;
            }
        }, ct);
    }

    public async IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var svcPwd = Environment.GetEnvironmentVariable(_config.ServiceAccountPasswordEnvVar);
        if (string.IsNullOrEmpty(svcPwd))
        {
            throw new InvalidOperationException(
                $"Service account password env var '{_config.ServiceAccountPasswordEnvVar}' not set");
        }

        using var conn = OpenConnection(_config.SearchTimeoutSeconds);
        conn.Bind(new NetworkCredential(_config.ServiceAccountDn, svcPwd));

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

    private LdapConnection OpenConnection(int timeoutSeconds)
    {
        var uri = new Uri(_config.LdapUrl);
        var identifier = new LdapDirectoryIdentifier(uri.Host, uri.Port);
        var conn = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        conn.SessionOptions.ProtocolVersion = 3;

        if (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase))
        {
            conn.SessionOptions.SecureSocketLayer = true;
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
