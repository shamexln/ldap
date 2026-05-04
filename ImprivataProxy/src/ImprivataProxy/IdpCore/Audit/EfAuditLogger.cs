using System.Text.Json;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Sources.Local.Entities;
using Microsoft.AspNetCore.Http;

namespace ImprivataProxy.IdpCore.Audit;

public class EfAuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor? _httpCtx;

    public EfAuditLogger(AppDbContext db, IHttpContextAccessor? httpCtx = null)
    {
        _db = db;
        _httpCtx = httpCtx;
    }

    public async Task LogAsync(
        string eventName,
        string? username = null,
        string? domain = null,
        string? clientIp = null,
        object? detail = null,
        CancellationToken ct = default)
    {
        // Auto-resolve the caller IP when we weren't given one explicitly.
        // Honors X-Forwarded-For (first entry) so we can log the real client behind a proxy.
        clientIp ??= ResolveClientIp();

        _db.AuditLog.Add(new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Event = eventName,
            Username = username,
            Domain = domain,
            ClientIp = clientIp,
            Detail = detail is null ? null : JsonSerializer.Serialize(detail),
        });
        await _db.SaveChangesAsync(ct);
    }

    private string? ResolveClientIp()
    {
        var ctx = _httpCtx?.HttpContext;
        if (ctx is null) return null;

        if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && xff.Count > 0)
        {
            var first = xff.ToString().Split(',', 2)[0].Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }

        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}
