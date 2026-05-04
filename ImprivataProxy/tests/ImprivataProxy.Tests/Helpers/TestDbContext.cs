using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ImprivataProxy.Tests.Helpers;

/// <summary>
/// Disposable SQLite in-memory DbContext factory.
/// Each instance owns its own connection + schema; disposing closes the in-memory DB.
/// </summary>
public sealed class TestDbContext : IDisposable
{
    private readonly SqliteConnection _conn;
    public AppDbContext Db { get; }

    public TestDbContext()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        Db = new AppDbContext(opts);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}
