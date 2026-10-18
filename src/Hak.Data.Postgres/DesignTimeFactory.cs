using Microsoft.EntityFrameworkCore.Design;

namespace Hak.Data.Postgres;

/// <summary>
/// Used only by `dotnet ef migrations add --project src/Hak.Data.Postgres --startup-project src/Hak.Data.Postgres`.
/// No connection is opened while generating migrations; the string just selects the provider.
/// </summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<HakDbContext>
{
    public HakDbContext CreateDbContext(string[] args)
        => new(HakDb.PostgresOptions("Host=127.0.0.1;Port=5433;Database=hak_design;Username=hak_design"));
}
