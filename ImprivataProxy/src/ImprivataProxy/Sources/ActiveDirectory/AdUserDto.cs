namespace ImprivataProxy.Sources.ActiveDirectory;

public sealed record AdUserDto(
    Guid ObjectGuid,
    string Username,
    string Domain,
    string DistinguishedName,
    string? DisplayName,
    string? GivenName,
    string? Sn,
    string? Mail,
    IReadOnlyList<string> Groups,
    bool Enabled);
