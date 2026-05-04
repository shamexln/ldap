using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.Sources.Local;

/// <summary>EF Core implementation of <see cref="IAuditStore"/>.</summary>
public class EfAuditStore : IAuditStore
{
    private readonly AppDbContext _db;

    public EfAuditStore(AppDbContext db) => _db = db;

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken ct)
    {
        _db.AuditLog.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
