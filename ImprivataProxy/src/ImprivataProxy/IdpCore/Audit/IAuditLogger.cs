namespace ImprivataProxy.IdpCore.Audit;

public interface IAuditLogger
{
    Task LogAsync(
        string eventName,
        string? username = null,
        string? domain = null,
        string? clientIp = null,
        object? detail = null,
        CancellationToken ct = default);
}
