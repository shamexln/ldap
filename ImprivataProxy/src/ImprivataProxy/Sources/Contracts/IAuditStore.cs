using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// ADR-0002 §8.2 fix: abstracts audit log persistence away from IdpCore.
/// Lets <see cref="ImprivataProxy.IdpCore.Audit.EfAuditLogger"/> stay in the
/// IdpCore layer (the audit *policy*) while keeping EF / DbContext concerns
/// (the audit *storage*) fully inside Sources.
/// </summary>
public interface IAuditStore
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken ct);
}
