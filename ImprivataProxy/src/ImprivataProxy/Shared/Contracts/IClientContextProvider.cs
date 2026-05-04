namespace ImprivataProxy.Shared.Contracts;

/// <summary>
/// ADR-0002 §8.2 fix: abstracts HTTP-specific caller metadata (currently just client IP).
/// IdpCore injects this to enrich audit events without taking a direct dependency on
/// ASP.NET Core's HTTP abstractions. The concrete ASP.NET implementation lives in
/// Shared/Http (Facade-adjacent), so IdpCore remains protocol-agnostic.
/// </summary>
public interface IClientContextProvider
{
    /// <summary>
    /// The current request's client IP, or null when there is no HTTP scope
    /// (e.g. background jobs like AD sync).
    /// </summary>
    string? GetClientIp();
}
