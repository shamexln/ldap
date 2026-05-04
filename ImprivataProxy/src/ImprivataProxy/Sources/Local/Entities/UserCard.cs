namespace ImprivataProxy.Sources.Local.Entities;

public class UserCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public User User { get; set; } = null!;

    public string CardUidHash { get; set; } = "";
    public string? CardUidLast4 { get; set; }
    public string? Label { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}
