using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>
/// Kiểm thử xuyên chu trình (tồn đọng 25/10): mỗi luồng chạy các tình huống lỗi giữa giao dịch, hai người cùng lúc, phiên bản cũ, thiếu quyền, đảo —
/// sau mỗi tình huống sổ cái / kho / công nợ vẫn khớp (LedgerInvariants).
/// </summary>
public partial class TradeTests
{
    private static InvoiceService InvFor(Scope s, TestHost h) => new(s.Db, s.Engine, s.Stock, s.Audit, h.Clock);

    [Fact]
    public async Task CrossCycle_Purchase_TwoUsersPostingTheSameInvoice_OneJournalEntry()
    {
        using var fx = await SetupAsync();
        var receipt = await DraftReceiptAsync(fx, 100, D(3, 2), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "X1", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        using var s1 = fx.H.NewScope(); using var s2 = fx.H.NewScope();
        var i1 = InvFor(s1, fx.H); var i2 = InvFor(s2, fx.H);
        var (ok, failed) = await ConcurrencyTests.RaceAsync(() => i1.PostAsync(fx.H.Approver, inv.Id), () => i2.PostAsync(fx.H.Approver, inv.Id));
        Assert.True(ok == 1, string.Join(" | ", failed.Select(f => f.ToString())));
        Assert.All(failed, f => Assert.IsType<DomainException>(f));
        using var check = fx.H.NewScope();
        Assert.Equal(1, await check.Db.JournalEntries.CountAsync(e => e.VoucherId == check.Db.CommercialInvoices.Where(i => i.Id == inv.Id).Select(i => i.VoucherId!.Value).First()));
        Assert.Equal(-5_500_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));
        await LedgerInvariants.AssertAsync(check.Db);
    }

    [Fact]
    public async Task CrossCycle_Sales_StaleRowVersion_Rejected_AndNothingIsWritten()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var issue = await PostedIssueAsync(fx, 10, D(3, 3), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TBB", "X2", D(3, 3), D(3, 3), "Bán", [new InvoiceLineInput(100_000, issue.Id, 1, VatRate: 10)]));
        var before = await fx.S.Db.JournalEntries.CountAsync();
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, inv.Id, expectedRowVersion: inv.RowVersion + 7)));
        Assert.Equal(before, await fx.S.Db.JournalEntries.CountAsync());
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id)).Status);
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id, expectedRowVersion: inv.RowVersion);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task CrossCycle_PermissionLost_PreparerAndAuditorCannotPostOrReverse()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var issue = await PostedIssueAsync(fx, 10, D(3, 3), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TBB", "X3", D(3, 3), D(3, 3), "Bán", [new InvoiceLineInput(100_000, issue.Id, 1, VatRate: 10)]));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => fx.Inv.PostAsync(fx.H.Auditor, inv.Id)));
        var posted = await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Auditor, posted.Id, D(3, 5), "thử")));
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id)).Status);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task CrossCycle_Purchase_FailureInsideThePostingTransaction_RollsBackEverything()
    {
        using var fx = await SetupAsync();
        var receipt = await DraftReceiptAsync(fx, 100, D(3, 2), fx.Add);
        await fx.S.Stock.PostDocumentAsync(fx.H.Approver, receipt.Id);                       // phiếu nhập giá 0 → hóa đơn 5.000.000 lệch giá, phải xác nhận
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "X4", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        var entries = await fx.S.Db.JournalEntries.CountAsync();
        var moves = await fx.S.Db.StockMovements.CountAsync();
        await Assert.ThrowsAnyAsync<DomainException>(() => fx.Inv.PostAsync(fx.H.Approver, inv.Id));
        fx.S.Db.ChangeTracker.Clear();
        Assert.Equal(entries, await fx.S.Db.JournalEntries.CountAsync());
        Assert.Equal(moves, await fx.S.Db.StockMovements.CountAsync());
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id)).Status);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task CrossCycle_Sales_CancelRacesWithPost_EndsInOneConsistentState()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var issue = await PostedIssueAsync(fx, 10, D(3, 3), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TBB", "X5", D(3, 3), D(3, 3), "Bán", [new InvoiceLineInput(100_000, issue.Id, 1, VatRate: 10)]));
        using var s1 = fx.H.NewScope(); using var s2 = fx.H.NewScope();
        var i1 = InvFor(s1, fx.H); var i2 = InvFor(s2, fx.H);
        var (ok, failed) = await ConcurrencyTests.RaceAsync(() => i1.PostAsync(fx.H.Approver, inv.Id), () => i2.CancelAsync(fx.H.Approver, inv.Id, "Hủy cùng lúc"));
        Assert.Equal(1, ok);
        Assert.All(failed, f => Assert.IsType<DomainException>(f));
        using var check = fx.H.NewScope();
        var final = await check.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
        Assert.Contains(final.Status, new[] { InvoiceStatus.Posted, InvoiceStatus.Cancelled });
        Assert.Equal(final.Status == InvoiceStatus.Posted ? 1_100_000m : 0m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        await LedgerInvariants.AssertAsync(check.Db);
    }

    [Fact]
    public async Task CrossCycle_Sales_ReverseThenReverseAgain_Rejected_AndBalancesReturnToZero()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var inv = await PostedSaleAsync(fx, "X6", 10, D(3, 3));
        await fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 6), "Đảo thử");
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 7), "Đảo lần hai")));
        Assert.Equal(0m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(0m, await fx.Bal("VATOUT"));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
