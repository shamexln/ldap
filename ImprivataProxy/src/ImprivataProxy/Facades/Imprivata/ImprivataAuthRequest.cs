namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Parsed Imprivata AuthUser request. Only fields used by this proxy are included.
/// </summary>
public sealed record ImprivataAuthRequest(
    string ModalityId,
    string? Username,
    string? Domain,
    string? Password,
    string? UniqueId,
    string? Pin,
    string? ServerState,
    bool CreateAuthTicket);
