namespace ImprivataProxy.Facades.Admin;

public sealed record UserListItemDto(
    string Id,
    string Username,
    string Domain,
    string? DisplayName,
    string? GivenName,
    string? Sn,
    bool Enabled,
    bool HasPin,
    int CardCount,
    DateTime? PwdLockedUntil,
    DateTime? PinLockedUntil,
    DateTime? LastSyncedAt,
    IReadOnlyList<string>? Groups);

public sealed record CardDto(
    string Id,
    string UserId,
    string? Label,
    string? Last4,
    DateTime IssuedAt,
    DateTime? ExpiresAt,
    bool Revoked);

public sealed record UserDetailDto(
    string Id,
    string Username,
    string Domain,
    string? DisplayName,
    string? GivenName,
    string? Sn,
    string? Upn,
    string? AdDistinguishedName,
    string? AdObjectGuid,
    bool Enabled,
    bool HasPin,
    int PwdFailCount,
    int PinFailCount,
    DateTime? PwdLockedUntil,
    DateTime? PinLockedUntil,
    DateTime? PwdHashUpdatedAt,
    DateTime? LastSyncedAt,
    IReadOnlyList<string>? Groups,
    IReadOnlyList<CardDto> Cards);

public sealed record PatchUserDto(bool? Enabled);

public sealed record SetPinDto(string Pin);

public sealed record IssueCardDto(
    string UserId,
    string CardUid,
    string? Label,
    DateTime? ExpiresAt);
