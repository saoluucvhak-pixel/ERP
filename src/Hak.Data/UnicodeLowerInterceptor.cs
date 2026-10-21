using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hak.Data;

/// <summary>
/// SQLite's built-in lower() folds ASCII only, so a case-insensitive search (EF translates ToLower() to lower()) misses Vietnamese
/// capitals: "Đ" never matches "đ", "VĂN" never matches "văn". PostgreSQL's lower() is Unicode-aware; replacing lower() on every
/// SQLite connection with .NET's invariant lowercasing makes both providers return the same rows. No index or trigger uses lower().
/// </summary>
public sealed class UnicodeLowerInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Register(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Register(connection);
        return Task.CompletedTask;
    }

    private static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
            sqlite.CreateFunction("lower", (string? s) => s?.ToLowerInvariant(), isDeterministic: true);
    }
}
