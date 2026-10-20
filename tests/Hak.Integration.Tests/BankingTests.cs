using Hak.Application.Accounting;
using Hak.Application.Banking;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using static Hak.Integration.Tests.Phase2Fixture;

namespace Hak.Integration.Tests;

public class BankingTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;
    private static readonly DateOnly Mar05 = new(2026, 3, 5);
    private static readonly DateOnly Mar10 = new(2026, 3, 10);

    private static async Task<(TestHost, Scope)> SetupAsync()
    {
        var h = await TestHost.CreateAsync();
        var s = h.NewScope();
        await ConfigureAsync(h, s);
        // Fund the bank account: customer transfer of 50,000,000 on 01/03.
        await PostDocAsync(h, s, Receipt(h, 50_000_000, new(2026, 3, 1), main: "BANK"));
        return (h, s);
    }

    private static async Task<Guid> SupplierInvoiceLineAsync(TestHost h, Scope s, decimal amount)
    {
        var inv = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 3, 2), new(2026, 3, 2), "Hóa đơn mua dịch vụ",
            [new(h.Acc["EXP"], Side.Debit, amount), new(h.Acc["AP"], Side.Credit, amount, h.Partner["NCC01"])]));
        return LineOf(s, inv.Id, h.Acc["AP"]);
    }

    private static PaymentOrderInput Order(TestHost h, decimal amount, Guid? settle = null)
        => new(h.Acc["BANK"], h.Partner["NCC01"], amount, Mar05, "Thanh toán hóa đơn dịch vụ", "0123456789", "NH TMCP A", settle);

    private static StatementInput Statement(TestHost h, decimal opening, string source, params StatementLineInput[] lines)
        => new(h.Acc["BANK"], new(2026, 3, 1), new(2026, 3, 31), opening, opening + lines.Sum(l => l.Amount), "sao-ke-03.csv", source, lines);

    [Fact]
    public async Task PayrollPayable_Queue_CreatesDraftOrders_OncePerLine()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        var svc = new Hak.Application.Cycles.PayrollPaymentService(s.Db, s.Settlement, s.PaymentOrders, s.Audit);
        Assert.Empty(await svc.QueueAsync(h.Auditor, Mar10));   // chưa ánh xạ vai trò → hàng chờ trống
        var prp = (await s.Setup.CreateAccountAsync(h.Admin, "PRP", "Phải trả người lao động (thử)", null, true, true, Hak.Domain.Catalog.BalanceDisplay.GrossByPartner,
            Hak.Domain.Catalog.OpenItemNature.Payable, Hak.Domain.Catalog.CashKind.None, Hak.Domain.Catalog.NegativeBalancePolicy.None)).Id;
        await s.Rules.SetRoleMappingAsync(h.Admin, "PAYROLL_PAYABLE", "PRP");
        var emp = (await s.Setup.CreatePartnerAsync(h.Admin, "NV01", "Nguyễn Văn A", Hak.Domain.Catalog.PartnerRoles.Employee)).Id;
        var sheet = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, Mar05, Mar05, "Lương tháng 02/2026",
            [new(h.Acc["EXP"], Side.Debit, 8_000_000), new(prp, Side.Credit, 8_000_000, emp)]));

        var row = Assert.Single(await svc.QueueAsync(h.Auditor, Mar10));
        Assert.Equal(("NV01", 8_000_000m, null), (row.EmployeeCode, row.Open, row.PaymentOrderNumber));

        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.CreateOrdersAsync(h.Auditor, h.Acc["BANK"], [row.JournalLineId], Mar10)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateOrdersAsync(h.Preparer, h.Acc["BANK"], [], Mar10)));
        var po = Assert.Single(await svc.CreateOrdersAsync(h.Preparer, h.Acc["BANK"], [row.JournalLineId], Mar10));
        Assert.Equal(("PAYROLL_PAYMENT", 8_000_000m, PaymentOrderStatus.Created, emp), (po.BusinessEvent, po.Amount, po.Status, po.PartnerId));
        Assert.Equal(po.Number, Assert.Single(await svc.QueueAsync(h.Auditor, Mar10)).PaymentOrderNumber);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => svc.CreateOrdersAsync(h.Preparer, h.Acc["BANK"], [row.JournalLineId], Mar10)));

        await s.PaymentOrders.CancelAsync(h.Preparer, po.Id, "Lập nhầm tài khoản chi");   // hủy lệnh → khoản lương lại chờ trả
        Assert.Null(Assert.Single(await svc.QueueAsync(h.Auditor, Mar10)).PaymentOrderNumber);
    }

    [Fact]
    public async Task PaymentOrder_FullLifecycle_ToReconciled_WithoutTouchingTheVoucher()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        var apLine = await SupplierInvoiceLineAsync(h, s, 12_000_000);

        var po = await s.PaymentOrders.CreateAsync(h.Preparer, Order(h, 12_000_000, apLine));
        Assert.StartsWith("UNC2026-", po.Number);
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => s.PaymentOrders.ApproveAsync(h.Preparer with { Permissions = new HashSet<string>(h.Preparer.Permissions) { "payment_order.approve" } }, po.Id)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.PaymentOrders.ApproveAsync(h.Auditor, po.Id)));
        await s.PaymentOrders.ApproveAsync(h.Approver, po.Id);
        await s.PaymentOrders.MarkSentAsync(h.Preparer, po.Id);
        Assert.False(await s.Db.Vouchers.AnyAsync(v => v.SourceSystem == PaymentOrder.SourceSystem)); // nothing accounted before execution

        var (order, voucher) = await s.PaymentOrders.MarkExecutedAsync(h.Preparer, po.Id, new(2026, 3, 6), "FT26065ABC123");
        Assert.Equal((PaymentOrderStatus.ExecutedByBank, VoucherStatus.Draft, VoucherTypes.BankPayment), (order.Status, voucher.Status, voucher.VoucherType));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.PaymentOrders.CancelAsync(h.Preparer, po.Id, "muộn")));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => s.PaymentOrders.RecreateVoucherAsync(h.Preparer, po.Id)));

        await s.Engine.SubmitAsync(h.Preparer, voucher.Id);
        await s.Engine.ApproveAsync(h.Approver, voucher.Id);
        await s.Engine.PostAsync(h.Approver, voucher.Id);
        Assert.Equal(PaymentOrderStatus.Posted, (await s.Db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await s.Settlement.OpenItemsAsync(h.Auditor, h.Acc["AP"], null, new(2026, 3, 31))); // invoice settled

        var before = await s.Db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);
        var st = await s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "file-1",
            new(new(2026, 3, 1), 50_000_000, "FT26060IN0001", "KH01 chuyen tien"),
            new(new(2026, 3, 6), -12_000_000, "FT26065ABC123", "TT hoa don dich vu")));
        var auto = await s.BankRecon.AutoMatchAsync(h.Approver, st.Id);
        // Payment: matched by bank reference (score 100). Receipt: the only same-amount, same-day candidate but no reference →
        // waits for a person to confirm (company decision 16/10/2026: no auto-match on amount and date alone).
        Assert.Equal((1, 1, 0), (auto.Matched, auto.Pending, auto.Unmatched));
        Assert.Equal(PaymentOrderStatus.Reconciled, (await s.Db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        var after = await s.Db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);
        Assert.Equal((before.RowVersion, before.Description, before.Status), (after.RowVersion, after.Description, after.Status));

        // A reconciled payment cannot be reversed until the match is undone.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, voucher.Id, new(2026, 3, 20), "sai")));
    }

    [Fact]
    public async Task CancellingUnexecutedOrder_CreatesNoAccountingEntry()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        var journalsBefore = await s.Db.JournalEntries.CountAsync();
        var po = await s.PaymentOrders.CreateAsync(h.Preparer, Order(h, 3_000_000));
        await s.PaymentOrders.ApproveAsync(h.Approver, po.Id);
        await s.PaymentOrders.MarkSentAsync(h.Preparer, po.Id);
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => s.PaymentOrders.CancelAsync(h.Preparer, po.Id, " ")));
        await s.PaymentOrders.CancelAsync(h.Preparer, po.Id, "Ngân hàng trả về do sai số tài khoản thụ hưởng");
        Assert.Equal(journalsBefore, await s.Db.JournalEntries.CountAsync());
        Assert.False(await s.Db.Vouchers.AnyAsync(v => v.SourceSystem == PaymentOrder.SourceSystem));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.PaymentOrders.MarkExecutedAsync(h.Preparer, po.Id, Mar10, "X")));
    }

    [Fact]
    public async Task ReversedPaymentVoucher_ReturnsOrderToExecuted_AndCanBeReaccountedOnce()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        var po = await s.PaymentOrders.CreateAsync(h.Preparer, Order(h, 2_000_000));
        await s.PaymentOrders.ApproveAsync(h.Approver, po.Id);
        await s.PaymentOrders.MarkSentAsync(h.Preparer, po.Id);
        var (_, v1) = await s.PaymentOrders.MarkExecutedAsync(h.Preparer, po.Id, Mar10, "FT1");
        await s.Engine.SubmitAsync(h.Preparer, v1.Id);
        await s.Engine.ApproveAsync(h.Approver, v1.Id);
        await s.Engine.PostAsync(h.Approver, v1.Id);

        await s.Engine.ReverseAsync(h.Approver, v1.Id, new(2026, 3, 12), "Hạch toán sai đối tượng");
        Assert.Equal(PaymentOrderStatus.ExecutedByBank, (await s.Db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        var v2 = await s.PaymentOrders.RecreateVoucherAsync(h.Preparer, po.Id);
        Assert.NotEqual(v1.Id, v2.Id);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => s.PaymentOrders.RecreateVoucherAsync(h.Preparer, po.Id)));
    }

    [Fact]
    public async Task StatementImport_ValidatesBalanceDuplicatesAndContinuity()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        var bad = Statement(h, 0, "f", new StatementLineInput(new(2026, 3, 1), 100, "A1", "x")) with { ClosingBalance = 99 };
        Assert.Equal(ErrorCodes.StatementUnbalanced, await CodeOf(() => s.BankRecon.ImportAsync(h.Approver, bad)));
        Assert.Equal(ErrorCodes.StatementDuplicate, await CodeOf(() => s.BankRecon.ImportAsync(h.Approver,
            Statement(h, 0, "f", new(new(2026, 3, 1), 100, "A1", "x"), new(new(2026, 3, 2), 5, "A1", "y")))));

        await s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "march", new StatementLineInput(new(2026, 3, 1), 100, "A1", "x")));
        Assert.Equal(ErrorCodes.StatementDuplicate, await CodeOf(() => s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "march", new StatementLineInput(new(2026, 3, 1), 100, "A1", "x")))));
        Assert.Equal(ErrorCodes.StatementDuplicate, await CodeOf(() => s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "other", new StatementLineInput(new(2026, 3, 1), 100, "A1", "x")))));

        var april = new StatementInput(h.Acc["BANK"], new(2026, 4, 1), new(2026, 4, 30), 777, 787, "april", "april", [new(new(2026, 4, 2), 10, "B1", "y")]);
        Assert.Equal(ErrorCodes.StatementUnbalanced, await CodeOf(() => s.BankRecon.ImportAsync(h.Approver, april))); // March closed at 100
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.BankRecon.ImportAsync(h.Auditor, april with { OpeningBalance = 100, ClosingBalance = 110 })));
        await s.BankRecon.ImportAsync(h.Approver, april with { OpeningBalance = 100, ClosingBalance = 110 });
    }

    [Fact]
    public async Task StatementLines_WithoutBankReference_GetStableAutoReference_AndAreNeverAutoMatchedByAmountAndDateAlone()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        await PostDocAsync(h, s, Receipt(h, 1_000_000, Mar10, main: "BANK"));
        // Ngân hàng không xuất mã giao dịch: một khoản thu khớp số tiền + ngày với bút toán, và hai khoản phí giống hệt nhau trong cùng tệp.
        var st = await s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "no-ref",
            new(Mar10, 1_000_000, "", "KH01 chuyen tien"),
            new(Mar10, -11_000, "", "Phi chuyen khoan"),
            new(Mar10, -11_000, "  ", "Phi chuyen khoan")));
        var refs = await s.Db.BankStatementLines.AsNoTracking().Where(l => l.StatementId == st.Id).Select(l => l.BankReference).ToListAsync();
        Assert.Equal(3, refs.Distinct().Count());
        Assert.All(refs, r => Assert.True(BankReconciliationService.IsAutoReference(r)));

        // Quyết định 16/10/2026: chỉ cùng tiền + ngày (không có tham chiếu) thì không tự khớp, chỉ gợi ý chờ xác nhận.
        var r = await s.BankRecon.AutoMatchAsync(h.Approver, st.Id);
        Assert.Equal((0, 1, 2), (r.Matched, r.Pending, r.Unmatched));

        // Cùng giao dịch xuất hiện lại ở sao kê chồng kỳ vẫn bị chặn nhập trùng.
        Assert.Equal(ErrorCodes.StatementDuplicate, await CodeOf(() => s.BankRecon.ImportAsync(h.Approver,
            Statement(h, 0, "overlap", new StatementLineInput(Mar10, 1_000_000, "", "KH01 chuyen tien")))));
    }

    [Fact]
    public async Task AmbiguousCandidates_GoToConfirmationQueue_AndReportExplainsDifferences()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        // Two identical customer transfers of 1,000,000 on the same day: the matcher must not guess.
        await PostDocAsync(h, s, Receipt(h, 1_000_000, Mar10, main: "BANK"));
        await PostDocAsync(h, s, Receipt(h, 1_000_000, Mar10, main: "BANK"));
        var st = await s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "amb",
            new(new(2026, 3, 1), 50_000_000, "IN-1", "KH01"),
            new(Mar10, 1_000_000, "IN-2", "KH01 lan 1"),
            new(new(2026, 3, 15), 250_000, "IN-3", "Lai tien gui")));
        var r = await s.BankRecon.AutoMatchAsync(h.Approver, st.Id);
        // 50M: single same-day candidate but no reference → queue. IN-2: two equal candidates → queue. IN-3 (interest): nothing booked yet.
        Assert.Equal((0, 2, 1), (r.Matched, r.Pending, r.Unmatched));
        var first = (await s.BankRecon.ReportAsync(h.Auditor, h.Acc["BANK"], new(2026, 3, 31))).StatementOnly.Single(x => x.BankReference == "IN-1");
        Assert.Single(first.Candidates);
        await s.BankRecon.ConfirmAsync(h.Approver, first.Id, first.Candidates[0].JournalLineId);

        var report = await s.BankRecon.ReportAsync(h.Auditor, h.Acc["BANK"], new(2026, 3, 31));
        var pending = report.StatementOnly.Single(x => x.BankReference == "IN-2");
        Assert.Equal(2, pending.Candidates.Count);
        await s.BankRecon.ConfirmAsync(h.Approver, pending.Id, pending.Candidates[0].JournalLineId);
        Assert.Equal(ErrorCodes.MatchInvalid, await CodeOf(() => s.BankRecon.ConfirmAsync(h.Approver,
            pending.Id, pending.Candidates[1].JournalLineId))); // already matched

        report = await s.BankRecon.ReportAsync(h.Auditor, h.Acc["BANK"], new(2026, 3, 31));
        Assert.Equal(52_000_000m, report.BookBalance);
        Assert.Equal(51_250_000m, report.StatementBalance);
        Assert.Contains(report.StatementOnly, x => x.BankReference == "IN-3"); // interest not yet booked
        Assert.Contains(report.BookOnly, x => x.SignedAmount == 1_000_000m);  // second receipt not on the statement
        Assert.Equal(0m, report.UnexplainedDifference);
    }

    [Fact]
    public async Task Unmatch_RequiresReason_IsOnce_AndRecordsCannotBeDeleted()
    {
        var (h, s) = await SetupAsync();
        using var _h = h; using var _s = s;
        var st = await s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "u", new StatementLineInput(new(2026, 3, 1), 50_000_000, "IN-9", "KH01")));
        var line = await s.Db.BankStatementLines.AsNoTracking().SingleAsync();
        var jl = (await s.BankRecon.ReportAsync(h.Auditor, h.Acc["BANK"], new(2026, 3, 31))).BookOnly.Single();
        Assert.Equal(ErrorCodes.MatchInvalid, await CodeOf(() => s.BankRecon.ConfirmAsync(h.Approver, line.Id, LineOf(s, s.Db.Vouchers.First(v => v.VoucherType == "BR").Id, h.Acc["AR"]))));
        var m = await s.BankRecon.ConfirmAsync(h.Approver, line.Id, jl.JournalLineId);

        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => s.BankRecon.UnmatchAsync(h.Approver, m.Id, "")));
        await s.BankRecon.UnmatchAsync(h.Approver, m.Id, "Ghép nhầm");
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.BankRecon.UnmatchAsync(h.Approver, m.Id, "lần 2")));
        Assert.Equal(StatementLineStatus.Unmatched, (await s.Db.BankStatementLines.AsNoTracking().SingleAsync()).Status);

        foreach (var sql in new[] { "DELETE FROM reconciliation_matches", "DELETE FROM bank_statement_lines", "UPDATE bank_statement_lines SET amount = 1", "UPDATE bank_statements SET closing_balance = 0" })
            Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync(sql))).Message);
        _ = st;
    }

    [Fact]
    public async Task BankOnlyLines_BecomeDraftVouchers_WithSuggestedFeeAccount_ThenAutoMatch()
    {
        var (h, s) = await SetupAsync();
        var st = await s.BankRecon.ImportAsync(h.Approver, Statement(h, 0, "file-fees",
            new StatementLineInput(new(2026, 3, 1), 50_000_000, "IN1", "KH chuyen tien"),
            new StatementLineInput(new(2026, 3, 31), -22_000, "FEE1", "Phí SMS-Banking tháng 3"),
            new StatementLineInput(new(2026, 3, 31), 15_000, "INT1", "Trả lãi tiền gửi")));
        await s.BankRecon.AutoMatchAsync(h.Approver, st.Id);
        var lines = await s.Db.BankStatementLines.AsNoTracking().Where(l => l.StatementId == st.Id).OrderBy(l => l.LineNo).ToListAsync();
        var fee = lines[1];

        var noRole = await s.StatementVouchers.SuggestAsync(h.Approver, fee.Id);
        Assert.Equal(("BANK_FEE", (Guid?)null), (noRole.RoleCode, noRole.AccountId));         // recognised, but the role is not mapped yet
        await s.Rules.SetRoleMappingAsync(h.Admin, "BANK_FEE", "EXP");
        Assert.Equal(h.Acc["EXP"], (await s.StatementVouchers.SuggestAsync(h.Approver, fee.Id)).AccountId);
        Assert.Equal("BANK_INTEREST_INCOME", (await s.StatementVouchers.SuggestAsync(h.Approver, lines[2].Id)).RoleCode);

        var v = await s.StatementVouchers.CreateDraftAsync(h.Preparer, fee.Id, h.Acc["EXP"], null, null);
        var draft = await s.Db.Vouchers.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == v.Id);
        Assert.Equal((VoucherStatus.Draft, new DateOnly(2026, 3, 31)), (draft.Status, draft.PostingDate));
        Assert.Contains(draft.Lines, l => l.AccountId == h.Acc["BANK"] && l.Side == Side.Credit && l.Amount == 22_000);
        Assert.Equal(ErrorCodes.DuplicateSource, (await Assert.ThrowsAsync<DomainException>(() =>
            s.StatementVouchers.CreateDraftAsync(h.Preparer, fee.Id, h.Acc["EXP"], null, null))).Code);

        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        await s.Engine.ApproveAsync(h.Approver, v.Id);
        await s.Engine.PostAsync(h.Approver, v.Id);
        await s.BankRecon.AutoMatchAsync(h.Approver, st.Id);
        Assert.Equal(StatementLineStatus.Matched, (await s.Db.BankStatementLines.AsNoTracking().SingleAsync(l => l.Id == fee.Id)).Status);
        Assert.Equal(ErrorCodes.MatchInvalid, (await Assert.ThrowsAsync<DomainException>(() =>
            s.StatementVouchers.CreateDraftAsync(h.Preparer, fee.Id, h.Acc["EXP"], null, null))).Code);
    }
}