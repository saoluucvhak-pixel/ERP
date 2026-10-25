using Hak.Data;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>
/// Bất biến chéo chu trình (kiểm tra 25/10/2026, phần B): sau MỘT luồng nghiệp vụ bất kỳ, sổ phải còn đúng — không phụ thuộc luồng đã đi qua đâu.
/// Một nguồn chỉ được ghi một lần; hai nơi giữ số dư/trạng thái độc lập thì phải khớp nhau.
/// </summary>
public static class LedgerInvariants
{
    public static async Task AssertAsync(HakDbContext db)
    {
        var lines = await db.JournalLines.AsNoTracking().Select(l => new { l.Id, l.JournalEntryId, l.Side, l.Amount }).ToListAsync();
        var entries = await db.JournalEntries.AsNoTracking().Select(e => new { e.Id, e.VoucherId, e.VoucherNumber }).ToListAsync();

        // 1. Mỗi bút toán cân Nợ = Có.
        foreach (var e in entries)
        {
            var mine = lines.Where(l => l.JournalEntryId == e.Id).ToList();
            var d = mine.Where(l => l.Side == Side.Debit).Sum(l => l.Amount);
            var c = mine.Where(l => l.Side == Side.Credit).Sum(l => l.Amount);
            Assert.True(d == c && d > 0, $"Bút toán {e.VoucherNumber} lệch hoặc rỗng: Nợ {d} / Có {c}");
        }
        // 2. Mỗi chứng từ chỉ có đúng một bút toán (không ghi hai lần cùng một nguồn).
        var dup = entries.GroupBy(e => e.VoucherId).Where(g => g.Count() > 1).Select(g => g.First().VoucherNumber).ToList();
        Assert.True(dup.Count == 0, "Chứng từ có hơn một bút toán: " + string.Join(", ", dup));
        // 3. Chứng từ đã ghi sổ thì có bút toán, và ngược lại bút toán chỉ thuộc chứng từ đã ghi sổ / đã đảo.
        var posted = await db.Vouchers.AsNoTracking().Where(v => v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed).Select(v => v.Id).ToListAsync();
        Assert.Empty(posted.Except(entries.Select(e => e.VoucherId)));
        Assert.Empty(entries.Select(e => e.VoucherId).Except(posted));

        // 4. Phân bổ công nợ còn hiệu lực không vượt số tiền của dòng nào (nợ mở không âm).
        var lineAmount = lines.ToDictionary(l => l.Id, l => l.Amount);
        var allocs = await db.SettlementAllocations.AsNoTracking().Where(a => a.VoidedAt == null).Select(a => new { a.DebitLineId, a.CreditLineId, a.Amount }).ToListAsync();
        foreach (var g in allocs.GroupBy(a => a.DebitLineId))
            Assert.True(g.Sum(a => a.Amount) <= lineAmount[g.Key], $"Phân bổ vượt dòng Nợ {g.Key}");
        foreach (var g in allocs.GroupBy(a => a.CreditLineId))
            Assert.True(g.Sum(a => a.Amount) <= lineAmount[g.Key], $"Phân bổ vượt dòng Có {g.Key}");

        // 5. Kho: tồn không âm theo kho–vật tư; mỗi biến động có tối đa một định giá còn hiệu lực.
        var stock = await db.StockMovements.AsNoTracking().GroupBy(m => new { m.WarehouseId, m.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Qty = g.Sum(m => m.Quantity) }).ToListAsync();
        Assert.True(stock.All(s => s.Qty >= 0), "Tồn kho âm sau luồng nghiệp vụ");
        var multi = await db.StockValuations.AsNoTracking().Where(v => v.VoidedAt == null).GroupBy(v => v.MovementId).Where(g => g.Count() > 1).CountAsync();
        Assert.Equal(0, multi);
    }
}
