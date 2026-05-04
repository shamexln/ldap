using ImprivataProxy.Shared.Contracts;
using Microsoft.AspNetCore.Http;

namespace ImprivataProxy.Shared.Http;

/// <summary>
/// ASP.NET Core adapter for <see cref="IClientContextProvider"/>.
/// Honors the first entry of X-Forwarded-For so we capture the real client IP when
/// the proxy sits behind a reverse proxy / load balancer; falls back to the TCP
/// remote address when the header is absent.
/// </summary>
public class HttpClientContextProvider : IClientContextProvider
{
    private readonly IHttpContextAccessor _http;

    public HttpClientContextProvider(IHttpContextAccessor http) => _http = http;

    public string? GetClientIp()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return null;

        if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && xff.Count > 0)
        {
            var first = xff.ToString().Split(',', 2)[0].Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }

        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}
