using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>--profile: SQL statements and time per posting step, and the slowest statements by total time.</summary>
sealed class SqlProfiler : DbCommandInterceptor
{
    private string step = "khác";
    private readonly Dictionary<string, (int Calls, int Commands, double Ms)> steps = new();
    private readonly Dictionary<string, (int Count, double Ms)> statements = new();
    private readonly Dictionary<DbCommand, long> started = new();

    public async Task<T> Step<T>(string name, Func<Task<T>> work)
    {
        var before = step;
        step = name;
        var commandsBefore = commandCount;
        var t = Stopwatch.GetTimestamp();
        try { return await work(); }
        finally
        {
            var s = steps.GetValueOrDefault(name);
            steps[name] = (s.Calls + 1, s.Commands + commandCount - commandsBefore, s.Ms + Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            step = before;
        }
    }

    private int commandCount;

    private void Begin(DbCommand c) { commandCount++; started[c] = Stopwatch.GetTimestamp(); }
    private void End(DbCommand c)
    {
        if (!started.Remove(c, out var t)) return;
        var key = step + " | " + string.Join(' ', c.CommandText.Split((char[])['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        if (key.Length > 160) key = key[..160];
        var s = statements.GetValueOrDefault(key);
        statements[key] = (s.Count + 1, s.Ms + Stopwatch.GetElapsedTime(t).TotalMilliseconds);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default) { Begin(c); return base.ReaderExecutingAsync(c, e, r, ct); }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c, CommandExecutedEventData e, DbDataReader r, CancellationToken ct = default) { End(c); return base.ReaderExecutedAsync(c, e, r, ct); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) { Begin(c); return base.NonQueryExecutingAsync(c, e, r, ct); }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c, CommandExecutedEventData e, int r, CancellationToken ct = default) { End(c); return base.NonQueryExecutedAsync(c, e, r, ct); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default) { Begin(c); return base.ScalarExecutingAsync(c, e, r, ct); }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand c, CommandExecutedEventData e, object? r, CancellationToken ct = default) { End(c); return base.ScalarExecutedAsync(c, e, r, ct); }

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("Bước | lần | lệnh SQL/lần | ms/lần");
        foreach (var (name, s) in steps.OrderBy(x => x.Key))
            Console.WriteLine($"{name} | {s.Calls} | {(double)s.Commands / s.Calls:N1} | {s.Ms / s.Calls:N2}");
        Console.WriteLine();
        Console.WriteLine("15 câu lệnh tốn thời gian nhất (tổng ms | số lần | câu lệnh):");
        foreach (var (k, s) in statements.OrderByDescending(x => x.Value.Ms).Take(15))
            Console.WriteLine($"{s.Ms,9:N0} | {s.Count,6} | {k}");
    }
}
