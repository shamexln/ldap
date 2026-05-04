namespace ImprivataProxy.Shared.Contracts;

public interface IAuditSink
{
    Task LogAsync(
        string eventName,
        string? username = null,
        string? domain = null,
        string? clientIp = null,
        object? detail = null,
        CancellationToken ct = default);
}
