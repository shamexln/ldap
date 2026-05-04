namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// ADR-0002 §4.1:抽象 "问外部身份源这个密码对不对" 的能力。
/// 今天由 <see cref="ActiveDirectory.LdapClient"/> 实现(LDAP simple bind);
/// 未来可替换成 SAML ECP / OIDC ROPC 等非 LDAP 实现,PwdAuthenticator 核心逻辑零改动。
/// </summary>
public interface IRemotePasswordVerifier
{
    Task<RemoteVerifyResult> VerifyAsync(
        string distinguishedName,
        string password,
        CancellationToken ct);
}

public enum RemoteVerifyOutcome
{
    /// <summary>外部系统验证密码正确。</summary>
    Valid,
    /// <summary>外部系统验证密码错误。</summary>
    Invalid,
    /// <summary>外部系统不可达(网络/证书/超时);调用方决定回退策略。</summary>
    Unreachable,
}

public record RemoteVerifyResult(RemoteVerifyOutcome Outcome, string? Diagnostic = null);
