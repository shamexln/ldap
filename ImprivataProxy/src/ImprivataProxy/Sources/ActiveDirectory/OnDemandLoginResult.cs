using ImprivataProxy.Sources.Contracts;

namespace ImprivataProxy.Sources.ActiveDirectory;

public sealed record OnDemandLoginResult(
    RemoteVerifyOutcome Outcome,
    AdUserDto? User = null,
    string? Diagnostic = null);
