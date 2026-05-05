using ImprivataProxy.Shared.Contracts;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// ADR-0002 §4.1:抽象 "问外部身份源这个密码对不对" 的能力。
///
/// 接受**协议中立的 <see cref="UserIdentity"/>**:具体实现按自己理解的字段解读
/// (LDAP → DistinguishedName;SAML ECP → UserPrincipalName;OIDC ROPC → Username/UPN)。
/// 这样 PwdAuthenticator 不用知道底层走的是 LDAP、SAML 还是 OIDC,
/// 换身份源时零改动。
///
/// 今天由 <see cref="ActiveDirectory.LdapClient"/> 实现(LDAP simple bind);
/// 未来可平行加 SamlEcpVerifier / OidcRopcVerifier 等实现,Program.cs 切换即可。
/// </summary>
public interface IRemotePasswordVerifier
{
    Task<RemoteVerifyResult> VerifyAsync(
        UserIdentity identity,
        string password,
        CancellationToken ct);
}

public enum RemoteVerifyOutcome
{
    /// <summary>外部系统验证密码正确。</summary>
    Valid,
    /// <summary>外部系统验证密码错误。</summary>
    Invalid,
    /// <summary>外部系统不可达(网络/证书/超时);调用方决定回退策略(通常返回 SystemError,不累计锁定)。</summary>
    Unreachable,
}

public record RemoteVerifyResult(RemoteVerifyOutcome Outcome, string? Diagnostic = null);
