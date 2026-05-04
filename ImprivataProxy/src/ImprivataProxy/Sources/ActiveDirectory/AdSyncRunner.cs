using System.Diagnostics;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;

namespace ImprivataProxy.Sources.ActiveDirectory;

/// <summary>
/// One-shot sync runner. Scoped (uses scoped AppDbContext via UserStore / IAuditSink).
/// Implements ADR-0002 §4.1 <see cref="IUserDirectorySync"/> contract;
/// future SCIM / Graph delta impls can replace this under the same interface.
/// </summary>
public class AdSyncRunner : IUserDirectorySync
{
    private readonly ILdapClient _ldap;
    private readonly IUserStore _users;
    private readonly IAuditSink _audit;
    private readonly ILogger<AdSyncRunner> _logger;

    public AdSyncRunner(
        ILdapClient ldap,
        IUserStore users,
        IAuditSink audit,
        ILogger<AdSyncRunner> logger)
    {
        _ldap = ldap;
        _users = users;
        _audit = audit;
        _logger = logger;
    }

    public async Task<SyncResult> RunOnceAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var seenGuids = new HashSet<string>();
        int added = 0, updated = 0, unchanged = 0;

        await foreach (var dto in _ldap.SearchAllUsersAsync(ct))
        {
            seenGuids.Add(dto.ObjectGuid.ToString());

            var outcome = await _users.UpsertFromAdAsync(dto, ct);
            switch (outcome)
            {
                case UpsertOutcome.Inserted: added++; break;
                case UpsertOutcome.Updated: updated++; break;
                case UpsertOutcome.Unchanged: unchanged++; break;
            }
        }

        // Only reached if the search completed without throwing.
        // If LDAP failed halfway, the caller catches and skips the disable step.
        var disabled = await _users.DisableUsersNotInAsync(seenGuids, ct);

        sw.Stop();
        var result = new SyncResult(added, updated, unchanged, disabled, sw.ElapsedMilliseconds);

        _logger.LogInformation(
            "AD sync completed: added={Added} updated={Updated} unchanged={Unchanged} disabled={Disabled} in {Ms}ms",
            added, updated, unchanged, disabled, sw.ElapsedMilliseconds);

        await _audit.LogAsync("ad_sync_completed", detail: result, ct: ct);

        return result;
    }
}
