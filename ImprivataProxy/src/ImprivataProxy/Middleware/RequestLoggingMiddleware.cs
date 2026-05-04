using ImprivataProxy.Shared.Logging;

namespace ImprivataProxy.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly LogSanitizer _sanitizer;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, LogSanitizer sanitizer)
    {
        _next = next;
        _logger = logger;
        _sanitizer = sanitizer;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        _logger.LogInformation(">> {Method} {Path}{Query}",
            request.Method, request.Path, request.QueryString);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var headers = _sanitizer.SanitizeHeaders(
                request.Headers.Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value!)));
            _logger.LogDebug(">> Headers: {Headers}", headers);
        }

        // Enable request body buffering so it can be read multiple times
        request.EnableBuffering();

        if (_logger.IsEnabled(LogLevel.Debug) && request.ContentLength > 0)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            var sanitizedBody = _sanitizer.SanitizeXml(body);
            _logger.LogDebug(">> Body: {Body}", sanitizedBody);
        }

        await _next(context);

        _logger.LogInformation("<< {StatusCode} for {Method} {Path}",
            context.Response.StatusCode, request.Method, request.Path);
    }
}
