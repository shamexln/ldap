namespace ImprivataProxy.Sources.Local.Entities;

public class TicketBlacklistEntry
{
    public string Jti { get; set; } = "";
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
