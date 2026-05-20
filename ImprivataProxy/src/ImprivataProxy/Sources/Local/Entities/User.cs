namespace ImprivataProxy.Sources.Local.Entities;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";

    public string? AdObjectGuid { get; set; }
    public string? AdDistinguishedName { get; set; }
    public string? DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? Sn { get; set; }

    public string? PinHash { get; set; }

    public int PinFailCount { get; set; }
    public DateTime? PinLockedUntil { get; set; }

    public int PwdFailCount { get; set; }
    public DateTime? PwdLockedUntil { get; set; }

    public bool Enabled { get; set; } = true;

    public string? AttributesJson { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<UserCard> Cards { get; set; } = new();
}
