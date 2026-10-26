using Hak.Data;
using Hak.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Common;

public static class DbExtensions
{
    /// <summary>Attempts for one unit of work when PostgreSQL aborts it (deadlock / serialization failure).</summary>
    public const int SerializationAttempts = 5;

    /// <summary>Key of the transaction-scoped advisory lock that makes PostgreSQL writers take turns (one writer at a time).</summary>
    public const long WriterLockKey = 0x48414B5752;   // "HAKWR"

    /// <summary>
    /// Runs <paramref name="work"/> in one ACID transaction with a single writer at a time, on both providers:
    /// SQLite takes BEGIN IMMEDIATE; PostgreSQL (LAN) takes a transaction-scoped advisory lock as the first statement, so
    /// concurrent writers queue instead of aborting, and every later statement (READ COMMITTED) sees all data committed
    /// before the lock was granted — check-then-write logic (numbering, audit chain, open amounts) stays correct. Reads
    /// outside a transaction stay concurrent. On failure everything is rolled back and the change tracker cleared, so callers
    /// never see half-applied state; a deadlock / serialization failure reruns the whole unit of work (it reloads what it
    /// reads). Storage errors are mapped to stable domain error codes.
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(this HakDbContext db, Func<Task<T>> work)
    {
        if (db.Database.CurrentTransaction is { } outer) return await InSavepointAsync(db, outer, work);
        for (var attempt = 1; ; attempt++)
        {
            var postgres = db.Database.IsNpgsql();
            await using var tx = postgres
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
                : await db.Database.BeginTransactionAsync();
            try
            {
                if (postgres) await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({WriterLockKey})");
                var result = await work();
                await tx.CommitAsync();
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                db.ChangeTracker.Clear();
                if (IsSerializationFailure(ex) && attempt < SerializationAttempts)
                {
                    await Task.Delay(Random.Shared.Next(10, 40) * attempt);   // jittered back-off spreads the retries apart
                    continue;
                }
                throw Map(ex);
            }
        }
    }

    /// <summary>
    /// Đơn vị công việc lồng trong một giao dịch có sẵn (ví dụ yêu cầu có Idempotency-Key): nếu lỗi thì quay về savepoint và xóa bộ theo dõi, đúng như khi chạy
    /// một mình — nếu không, phần đã ghi dở của bước lỗi vẫn bị commit cùng giao dịch ngoài khi người gọi bắt lỗi rồi đi tiếp. Lỗi vẫn ném lại nguyên dạng
    /// (giao dịch ngoài tự ánh xạ và quyết định rollback toàn bộ). Thay đổi đang chờ của giao dịch ngoài được ghi xuống trước savepoint để không bị xóa theo.
    /// </summary>
    private static async Task<T> InSavepointAsync<T>(HakDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Func<Task<T>> work)
    {
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        var name = "hak_sp_" + Guid.NewGuid().ToString("N");
        await tx.CreateSavepointAsync(name);
        try
        {
            var result = await work();
            await tx.ReleaseSavepointAsync(name);
            return result;
        }
        catch
        {
            try { await tx.RollbackToSavepointAsync(name); } catch { /* giao dịch ngoài đã hỏng: nó sẽ tự rollback */ }
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool IsSerializationFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Npgsql.PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }

    public static Task InTransactionAsync(this HakDbContext db, Func<Task> work)
        => db.InTransactionAsync(async () => { await work(); return true; });

    private static Exception Map(Exception ex)
    {
        if (ex is DomainException) return ex;
        if (ex is DbUpdateConcurrencyException)
            return new DomainException(ErrorCodes.ConcurrencyConflict, "Dữ liệu đã bị người khác thay đổi. Vui lòng tải lại.");
        var inner = ex.InnerException ?? ex;
        var msg = inner.Message;
        if (IsSerializationFailure(ex))
            return new DomainException(ErrorCodes.ConcurrencyConflict, "Dữ liệu đang được người khác cập nhật cùng lúc; vui lòng thử lại.");
        if (msg.Contains("LEDGER_IMMUTABLE"))
            return new DomainException(ErrorCodes.LedgerImmutable, "Sổ cái và chứng từ đã ghi sổ không được sửa hoặc xóa.");
        // SQLite: "UNIQUE constraint failed: table.col, …"; PostgreSQL 23505: "… unique constraint \"ix_table_cols\"".
        if (msg.Contains("UNIQUE constraint failed") || inner is Npgsql.PostgresException { SqlState: "23505" })
        {
            if (msg.Contains("journal_entries")) return new DomainException(ErrorCodes.AlreadyPosted, "Chứng từ đã được ghi sổ.");
            if (msg.Contains("reverses_voucher_id")) return new DomainException(ErrorCodes.InvalidStateTransition, "Chứng từ đã được đảo.");
            if (msg.Contains("vouchers") && msg.Contains("source_id")) return new DomainException(ErrorCodes.DuplicateSource, "Nghiệp vụ nguồn đã được ghi nhận.");
            if (msg.Contains("import_batches")) return new DomainException(ErrorCodes.PackageReplayed, "Gói dữ liệu đã được nhập.");
            return new DomainException(ErrorCodes.DuplicateCode, "Mã đã tồn tại.");
        }
        return ex;
    }
}

public sealed class HakRuntimeOptions
{
    /// <summary>Development | Test | Production (master prompt §4.5). Defaults to Production so a missing setting fails closed.</summary>
    public string Environment { get; set; } = "Production";
    public bool IsProduction => string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(10);
    /// <summary>Failed logins in a row FROM ONE SOURCE before that source is locked out of the account for <see cref="LockoutDuration"/>.</summary>
    public int MaxFailedLogins { get; set; } = 5;
    /// <summary>Failed logins in a row from all sources together before the whole account is locked (backstop against distributed guessing).</summary>
    public int MaxFailedLoginsAllSources { get; set; } = 20;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    public int PasswordIterations { get; set; } = Security.PasswordHasher.DefaultIterations;
}
