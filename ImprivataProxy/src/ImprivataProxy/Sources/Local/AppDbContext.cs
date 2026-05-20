using ImprivataProxy.Sources.Local.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Sources.Local;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserCard> UserCards => Set<UserCard>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<TicketBlacklistEntry> TicketBlacklist => Set<TicketBlacklistEntry>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).HasMaxLength(64);
            b.Property(u => u.Username).HasMaxLength(128).IsRequired();
            b.Property(u => u.Domain).HasMaxLength(128).IsRequired();
            b.Property(u => u.AdObjectGuid).HasMaxLength(64);
            b.Property(u => u.AdDistinguishedName).HasMaxLength(512);
            b.Property(u => u.DisplayName).HasMaxLength(256);
            b.Property(u => u.PinHash).HasMaxLength(512);

            b.HasIndex(u => new { u.Username, u.Domain }).IsUnique();
            b.HasIndex(u => u.AdObjectGuid).IsUnique();

            b.HasMany(u => u.Cards)
             .WithOne(c => c.User)
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<UserCard>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasMaxLength(64);
            b.Property(c => c.UserId).HasMaxLength(64).IsRequired();
            b.Property(c => c.CardUidHash).HasMaxLength(128).IsRequired();
            b.Property(c => c.CardUidLast4).HasMaxLength(16);
            b.Property(c => c.Label).HasMaxLength(128);

            b.HasIndex(c => c.CardUidHash).IsUnique();
        });

        mb.Entity<AuthSession>(b =>
        {
            b.HasKey(s => s.ServerState);
            b.Property(s => s.ServerState).HasMaxLength(128);
            b.Property(s => s.UserId).HasMaxLength(64).IsRequired();
            b.Property(s => s.Stage).HasMaxLength(32).IsRequired();
            b.Property(s => s.PendingModality).HasMaxLength(16).IsRequired();

            b.HasIndex(s => s.ExpiresAt);
        });

        mb.Entity<TicketBlacklistEntry>(b =>
        {
            b.HasKey(t => t.Jti);
            b.Property(t => t.Jti).HasMaxLength(64);
            b.HasIndex(t => t.ExpiresAt);
        });

        mb.Entity<AuditLogEntry>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Event).HasMaxLength(64).IsRequired();
            b.Property(a => a.Username).HasMaxLength(128);
            b.Property(a => a.Domain).HasMaxLength(128);
            b.Property(a => a.ClientIp).HasMaxLength(64);

            b.HasIndex(a => a.Timestamp);
            b.HasIndex(a => a.Event);
        });
    }
}
