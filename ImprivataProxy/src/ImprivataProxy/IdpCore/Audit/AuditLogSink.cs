using System.Text.Json;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.IdpCore.Audit;

/// <summary>
/// Default <see cref="IAuditSink"/>. Builds an <see cref="AuditLogEntry"/>
/// from the call + ambient client context and hands it off to <see cref="IAuditStore"/>
/// for persistence. No direct DbContext or HttpContext dependency —— see ADR-0002 §8.2.
/// </summary>
public class AuditLogSink : IAuditSink
{
    private readonly IAuditStore _store;
    private readonly IClientContextProvider? _clientCtx;

    public AuditLogSink(IAuditStore store, IClientContextProvider? clientCtx = null)
    {
        _store = store;
        _clientCtx = clientCtx;
    }

    public async Task LogAsync(
        string eventName,
        string? username = null,
        string? domain = null,
        string? clientIp = null,
        object? detail = null,
        CancellationToken ct = default)
    {
        // Fall back to the ambient context (HTTP request, if any) when caller didn't supply one.
        clientIp ??= _clientCtx?.GetClientIp();

        await _store.AppendAsync(new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Event = eventName,
            Username = username,
            Domain = domain,
            ClientIp = clientIp,
            Detail = detail is null ? null : JsonSerializer.Serialize(detail),
        }, ct);
    }
}
