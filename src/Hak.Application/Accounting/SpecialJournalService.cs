using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Ledger;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Accounting;

public sealed record SpecialJournalLine(int LineNo, string AccountCode, string? PartnerCode, decimal Debit, decimal Credit, string? Description);
public sealed record SpecialJournalEntry(DateOnly PostingDate, string VoucherType, string VoucherNumber, Guid VoucherId, string Description, decimal Amount, bool Duplicate,
    SpecialJournal MainJournal, string MainJournalName, IReadOnlyList<SpecialJournal> AlsoIn, IReadOnlyList<SpecialJournalLine> Lines);
public sealed record SpecialJournalBook(SpecialJournal Journal, string Name, IReadOnlyList<SpecialJournalEntry> Entries, decimal Total, int Count, int DuplicateCount);
/// <summary>Đối chiếu: tổng Nhật ký (mọi bút toán đã ghi sổ) = tổng các sổ sau khi loại bút toán trùng.</summary>
public sealed record SpecialJournalReport(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<SpecialJournalBook> Books, decimal JournalTotal, decimal BooksTotal, bool Balanced, int DuplicateEntries);

/// <summary>
/// Sổ nhật ký đặc biệt (bán hàng, mua hàng, thu tiền, chi tiền) và Nhật ký chung của các nghiệp vụ còn lại, lập từ cùng một Nhật ký. Xử lý bút toán trùng theo <see cref="SpecialJournals"/>:
/// mỗi bút toán chỉ tính vào tổng cộng của sổ chính; ở sổ khác hiện kèm dấu "trùng". Không ghi thêm dữ liệu.
/// </summary>
public sealed class SpecialJournalService(HakDbContext db)
{
    public async Task<SpecialJournalReport> ReportAsync(SessionContext ctx, DateOnly fromDate, DateOnly toDate)
    {
        ctx.Require(Permissions.ReportView);
        var cid = ctx.CompanyId;
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == cid).ToDictionaryAsync(a => a.Id);
        var roles = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == cid).ToListAsync();
        HashSet<Guid> Role(params string[] codes) => roles.Where(r => codes.Contains(r.RoleCode)).Select(r => r.AccountId).ToHashSet();
        var inventory = (await db.Items.AsNoTracking().Where(i => i.CompanyId == cid && i.InventoryAccountId != null).Select(i => i.InventoryAccountId!.Value).Distinct().ToListAsync()).ToHashSet();
        var context = new SpecialJournalContext(accounts.Values.Where(a => a.CashKind != Hak.Domain.Catalog.CashKind.None).Select(a => a.Id).ToHashSet(), inventory, Role("SALES_REVENUE"), Role("AP_TRADE", "ADVANCE", "GRNI"));

        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == cid).ToDictionaryAsync(p => p.Id, p => p.Code);
        var raw = await (from l in db.JournalLines.AsNoTracking()
                         join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                         where l.CompanyId == cid && l.PostingDate >= fromDate && l.PostingDate <= toDate
                         orderby l.PostingDate, e.VoucherNumber, l.LineNo
                         select new { e.Id, e.PostingDate, e.VoucherType, e.VoucherNumber, e.VoucherId, e.Description, l.LineNo, l.AccountId, l.PartnerId, l.Side, l.Amount, LineDescription = l.Description }).ToListAsync();

        var entries = new List<SpecialJournalEntry>();
        var perJournal = Enum.GetValues<SpecialJournal>().ToDictionary(j => j, _ => new List<SpecialJournalEntry>());
        foreach (var g in raw.GroupBy(r => r.Id))
        {
            var first = g.First();
            var touched = SpecialJournals.Touched(first.VoucherType, g.Select(r => (r.AccountId, r.Side)).ToList(), context);
            var main = SpecialJournals.Main(touched);
            var lines = g.Select(r => new SpecialJournalLine(r.LineNo, accounts[r.AccountId].Code, r.PartnerId is { } p ? partners.GetValueOrDefault(p) : null,
                r.Side == Side.Debit ? r.Amount : 0, r.Side == Side.Credit ? r.Amount : 0, r.LineDescription)).ToList();
            var amount = lines.Sum(l => l.Debit);
            var homes = touched.Count == 0 ? [SpecialJournal.General] : touched;
            foreach (var j in homes)
                perJournal[j].Add(new SpecialJournalEntry(first.PostingDate, first.VoucherType, first.VoucherNumber, first.VoucherId, first.Description, amount, j != main, main, SpecialJournals.Name(main),
                    homes.Where(x => x != j).ToList(), lines));
            entries.Add(perJournal[main].Last());
        }
        var books = new[] { SpecialJournal.Sales, SpecialJournal.Purchases, SpecialJournal.CashReceipts, SpecialJournal.CashPayments, SpecialJournal.General }
            .Select(j => new SpecialJournalBook(j, SpecialJournals.Name(j), perJournal[j], perJournal[j].Where(e => !e.Duplicate).Sum(e => e.Amount), perJournal[j].Count(e => !e.Duplicate), perJournal[j].Count(e => e.Duplicate))).ToList();
        var journalTotal = raw.Where(r => r.Side == Side.Debit).Sum(r => r.Amount);
        var booksTotal = books.Sum(b => b.Total);
        return new SpecialJournalReport(fromDate, toDate, books, journalTotal, booksTotal, journalTotal == booksTotal, books.Sum(b => b.DuplicateCount));
    }
}
