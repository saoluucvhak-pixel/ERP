using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hak.Data;

public static class HakDb
{
    public static DbContextOptions<HakDbContext> SqliteOptions(string databasePath, params IInterceptor[] extraInterceptors)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();
        return new DbContextOptionsBuilder<HakDbContext>()
            .UseSqlite(cs)
            .AddInterceptors([new LedgerImmutabilityInterceptor(), new UnicodeLowerInterceptor(), .. extraInterceptors])
            .Options;
    }

    public const string PostgresMigrationsAssembly = "Hak.Data.Postgres";

    /// <summary>PostgreSQL (LAN server mode). Migrations live in Hak.Data.Postgres because the SQL differs per provider (docs/02 ADR-04).</summary>
    public static DbContextOptions<HakDbContext> PostgresOptions(string connectionString, params IInterceptor[] extraInterceptors)
        => new DbContextOptionsBuilder<HakDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsAssembly(PostgresMigrationsAssembly))
            .AddInterceptors([new LedgerImmutabilityInterceptor(), .. extraInterceptors])
            .Options;

    /// <summary>Applies migrations and enables WAL. Never deletes data.</summary>
    public static void Migrate(HakDbContext db)
    {
        db.Database.Migrate();
        if (db.Database.IsSqlite())
        {
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.Database.ExecuteSqlRaw("PRAGMA synchronous=FULL;");
        }
    }
}

/// <summary>Used by `dotnet ef migrations add` only.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<HakDbContext>
{
    public HakDbContext CreateDbContext(string[] args) => new(HakDb.SqliteOptions("design-time.db"));
}
