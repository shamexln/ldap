namespace ImprivataProxy.Shared.Contracts;

/// <summary>
/// Protocol-neutral user identity. Each <see cref="IRemotePasswordVerifier"/>
/// implementation picks the field its protocol understands:
/// <list type="bullet">
///   <item>LDAP / AD bind      → <see cref="DistinguishedName"/></item>
///   <item>SAML ECP (future)   → <see cref="UserPrincipalName"/></item>
///   <item>OIDC ROPC (future)  → <see cref="UserPrincipalName"/> or <see cref="Username"/></item>
/// </list>
/// Nullable fields allow one source type (e.g. Sources.Local.Entities.User)
/// to feed any verifier; unknown-to-this-source fields stay null.
/// </summary>
public record UserIdentity(
    string Username,
    string Domain,
    string? DistinguishedName = null,
    string? UserPrincipalName = null,
    string? ObjectGuid = null);
