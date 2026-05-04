using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.IdpCore.Authentication;

/// <summary>
/// Discriminated result of an authentication attempt.
/// Endpoints translate this into Imprivata Response XML.
/// </summary>
public abstract record AuthResult
{
    /// <summary>Authentication succeeded. Ticket is included.</summary>
    public sealed record Success(User User, string Ticket) : AuthResult;

    /// <summary>Multi-step authentication in progress. Client must come back with ServerState.</summary>
    public sealed record Pending(string ServerState, string PendingModality) : AuthResult;

    /// <summary>Authentication failed. Rtc is the specific reason.</summary>
    public sealed record Failure(int Rtc, string Reason) : AuthResult;
}
