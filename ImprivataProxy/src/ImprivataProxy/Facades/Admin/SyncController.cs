using ImprivataProxy.Sources.ActiveDirectory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImprivataProxy.Facades.Admin;

[ApiController]
[Route("admin/sync")]
[Authorize(AuthenticationSchemes = AdminAuthenticationHandler.SchemeName)]
public class SyncController : ControllerBase
{
    private readonly AdSyncService? _sync;

    public SyncController(AdSyncService? sync = null) => _sync = sync;

    /// <summary>
    /// Manually trigger one AD sync run. Returns 200 + stats when complete,
    /// or 409 if another run is already in progress (semaphore held).
    /// Returns 404 when running in OnDemand mode (sync service not registered).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Trigger(CancellationToken ct)
    {
        if (_sync is null)
        {
            return NotFound(new { error = "sync not available in OnDemand mode" });
        }

        var result = await _sync.TriggerAsync(ct);
        if (result is null)
        {
            return Conflict(new { error = "sync already running or failed; see logs" });
        }
        return Ok(result);
    }
}
