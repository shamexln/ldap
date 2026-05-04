namespace ImprivataProxy.Sources.Local.Entities;

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Event { get; set; } = "";
    public string? Username { get; set; }
    public string? Domain { get; set; }
    public string? ClientIp { get; set; }
    public string? Detail { get; set; }
}
