namespace ImprivataProxy.Sources.Local.Entities;

public class AuthSession
{
    public string ServerState { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string PendingModality { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
