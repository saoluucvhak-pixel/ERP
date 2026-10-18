using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>--profile: time spent opening connections, beginning and committing transactions.</summary>
sealed class TxProfiler : IDbTransactionInterceptor, IDbConnectionInterceptor
{
    private readonly Dictionary<string, (int Count, double Ms)> totals = new();
    private long t;
    private void Add(string k) { var s = totals.GetValueOrDefault(k); totals[k] = (s.Count + 1, s.Ms + Stopwatch.GetElapsedTime(t).TotalMilliseconds); }

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection c, TransactionStartingEventData e, InterceptionResult<DbTransaction> r, CancellationToken ct = default)
    { t = Stopwatch.GetTimestamp(); return ValueTask.FromResult(r); }
    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection c, TransactionEndEventData e, DbTransaction r, CancellationToken ct = default)
    { Add("BEGIN"); return ValueTask.FromResult(r); }
    public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx, TransactionEventData e, InterceptionResult r, CancellationToken ct = default)
    { t = Stopwatch.GetTimestamp(); return ValueTask.FromResult(r); }
    public Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData e, CancellationToken ct = default)
    { Add("COMMIT"); return Task.CompletedTask; }
    public ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c, ConnectionEventData e, InterceptionResult r, CancellationToken ct = default)
    { t = Stopwatch.GetTimestamp(); return ValueTask.FromResult(r); }
    public Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default)
    { Add("OPEN"); return Task.CompletedTask; }

    public void Print()
    {
        foreach (var (k, s) in totals) Console.WriteLine($"{k}: {s.Count} lần, trung bình {s.Ms / s.Count:N2} ms");
    }
}
