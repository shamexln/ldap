using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Configuration;
using Microsoft.Extensions.Options;

namespace ImprivataProxy.Sources.ActiveDirectory;

/// <summary>
/// BackgroundService that runs AdSyncRunner on a periodic timer.
/// Exposes TriggerAsync for admin-initiated manual runs.
/// Uses Semaphore(1,1) to prevent overlapping runs.
/// On failure: logs + audit; does NOT disable any users.
/// </summary>
public class AdSyncService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly AdConfig _config;
    private readonly ILogger<AdSyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AdSyncService(
        IServiceProvider sp,
        IOptions<AdConfig> config,
        ILogger<AdSyncService> logger)
    {
        _sp = sp;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the DB initialization and host startup settle before first run.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.SyncIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            await TriggerAsync(stoppingToken);

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Run one sync now (or return null if another run is already in progress).
    /// Caller is responsible for handling null as "skipped".
    /// </summary>
    public async Task<SyncResult?> TriggerAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            _logger.LogInformation("AD sync already running; skipping this trigger");
            return null;
        }

        try
        {
            using var scope = _sp.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AdSyncRunner>();
            return await runner.RunOnceAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AD sync failed");

            // Best-effort audit; don't let audit failure mask the original error.
            try
            {
                using var scope = _sp.CreateScope();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditSink>();
                await audit.LogAsync("ad_sync_failed",
                    detail: new { error = ex.Message, type = ex.GetType().Name },
                    ct: ct);
            }
            catch { /* swallow */ }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
