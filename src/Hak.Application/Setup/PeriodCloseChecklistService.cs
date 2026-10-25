using Hak.Application.Assets;
using Hak.Application.Inventory;
using Hak.Application.Loans;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

/// <param name="Severity">Blocking: sai lệch phải xử lý trước khi khóa kỳ. Confirm: còn việc dở, người khóa kỳ phải biết rõ. CarryOver: số chuyển kỳ hợp lệ (không buộc bằng 0). Ok: đạt.</param>
public sealed record CloseCheckItem(string Code, string Severity, string Title, int Count, decimal? Amount, string Detail, string Screen);

public sealed record PeriodCloseChecklist(Guid PeriodId, int PeriodNo, DateOnly From, DateOnly To, string Status, bool CanLock, IReadOnlyList<CloseCheckItem> Items);

/// <summary>
/// Bảng kiểm đóng kỳ (Đợt 5): đối chiếu kho–sổ cái, công nợ–sổ cái, tài sản–sổ cái, vay–sổ cái, xuất kho chờ tính giá, chứng từ chưa xử lý, sao kê chưa khớp. Phân biệt rõ
/// LỖI CHẶN (sai lệch số liệu) với việc cần xác nhận và khoản CHUYỂN KỲ HỢP LỆ — hàng về chưa có hóa đơn (GRNI), hàng giao chưa lập hóa đơn, tiền đang chuyển: không buộc bằng 0.
/// Chỉ đọc. Khóa kỳ theo bảng kiểm (<see cref="LockCheckedAsync"/>) từ chối khi còn lỗi chặn; lệnh khóa cũ giữ nguyên để không đổi hành vi của người đang dùng.
/// </summary>
public sealed class PeriodCloseChecklistService(HakDbContext db, StockService stock, SettlementService settlement, AssetService assets, LoanService loans, InvoiceService invoices, SetupService setup, Hak.Application.Tax.VatOffsetService vat)
{
    public async Task<PeriodCloseChecklist> ForPeriodAsync(SessionContext ctx, Guid periodId)
    {
        ctx.Require(Permissions.ReportView);
        var p = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId && x.CompanyId == ctx.CompanyId)
                ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại trong công ty.");
        var (periodStart, periodEnd) = (p.StartDate, p.EndDate);
        var items = new List<CloseCheckItem>();
        void Add(string code, string severity, string title, int count, decimal? amount, string detail, string screen) => items.Add(new(code, severity, title, count, amount, detail, screen));
        static string Sev(bool bad, string severity) => bad ? severity : "Ok";

        // ---- chứng từ chưa xử lý
        var pending = await db.Vouchers.CountAsync(v => v.CompanyId == ctx.CompanyId && v.PostingDate >= periodStart && v.PostingDate <= periodEnd && (v.Status == VoucherStatus.Submitted || v.Status == VoucherStatus.Approved));
        Add("VOUCHERS_PENDING", Sev(pending > 0, "Blocking"), "Chứng từ đã gửi duyệt hoặc đã duyệt nhưng chưa ghi sổ", pending, null, pending > 0 ? "Duyệt và ghi sổ, hoặc từ chối trước khi khóa kỳ." : "", "vouchers");
        var drafts = await db.Vouchers.CountAsync(v => v.CompanyId == ctx.CompanyId && v.PostingDate >= periodStart && v.PostingDate <= periodEnd && v.Status == VoucherStatus.Draft);
        Add("VOUCHERS_DRAFT", Sev(drafts > 0, "Confirm"), "Chứng từ nháp trong kỳ", drafts, null, drafts > 0 ? "Sau khi khóa kỳ không ghi sổ được vào kỳ này; ghi sổ hoặc hủy nháp." : "", "vouchers");
        var stockDrafts = await db.StockDocuments.CountAsync(d => d.CompanyId == ctx.CompanyId && d.PostingDate >= periodStart && d.PostingDate <= periodEnd && d.Status == StockDocumentStatus.Draft);
        Add("STOCK_DRAFT", Sev(stockDrafts > 0, "Confirm"), "Phiếu kho nháp trong kỳ", stockDrafts, null, stockDrafts > 0 ? "Phiếu nháp chưa tác động kho và sổ cái." : "", "stock");
        var invoiceDrafts = await db.CommercialInvoices.CountAsync(i => i.CompanyId == ctx.CompanyId && i.PostingDate >= periodStart && i.PostingDate <= periodEnd && i.Status == InvoiceStatus.Draft);
        Add("INVOICE_DRAFT", Sev(invoiceDrafts > 0, "Confirm"), "Hóa đơn nháp trong kỳ", invoiceDrafts, null, invoiceDrafts > 0 ? "Hóa đơn nháp chưa vào công nợ, thuế và sổ cái." : "", "trade");

        // ---- kho – sổ cái, định giá chờ
        var inventory = await stock.ReconcileAsync(ctx, periodEnd);
        var invDiff = inventory.Where(r => r.Difference != 0).ToList();
        Add("INVENTORY_GL", Sev(invDiff.Count > 0, "Blocking"), "Giá trị kho khớp sổ cái", invDiff.Count, invDiff.Sum(r => Math.Abs(r.Difference)),
            string.Join("; ", invDiff.Select(r => $"TK {r.AccountCode}: kho {r.StockValue} ≠ sổ {r.LedgerBalance}")), "stock");
        var pendingIssue = inventory.Sum(r => r.PendingIssueQty);
        Add("COSTING_PENDING", Sev(pendingIssue > 0, "Blocking"), "Xuất kho chờ tính giá (bình quân cuối tháng)", pendingIssue > 0 ? 1 : 0, pendingIssue, pendingIssue > 0 ? $"Còn {pendingIssue} chưa có giá vốn; tính giá kỳ trước khi khóa." : "", "stock");

        // ---- công nợ, tài sản, vay – sổ cái
        var sub = await settlement.ReconcileAsync(ctx, periodEnd);
        Add("SUBLEDGER_GL", Sev(!sub.IsReconciled, "Blocking"), "Sổ chi tiết công nợ khớp sổ cái", sub.Rows.Count, null,
            string.Join("; ", sub.Rows.Take(5).Select(r => $"TK {r.AccountCode}: {r.Issue ?? $"lệch {r.Difference}"}")), "arap");
        var assetRows = (await assets.ReconcileAsync(ctx, periodEnd)).Where(r => r.Difference != 0).ToList();
        Add("ASSET_GL", Sev(assetRows.Count > 0, "Blocking"), "Thẻ tài sản / CCDC khớp sổ cái", assetRows.Count, assetRows.Sum(r => Math.Abs(r.Difference)),
            string.Join("; ", assetRows.Select(r => $"TK {r.AccountCode} ({r.Kind}): thẻ {r.RegisterBalance} ≠ sổ {r.LedgerBalance}")), "assets");
        var loanRows = (await loans.ReconcileAsync(ctx, periodEnd)).Where(r => r.Difference != 0).ToList();
        Add("LOAN_GL", Sev(loanRows.Count > 0, "Blocking"), "Sổ vay khớp sổ cái", loanRows.Count, loanRows.Sum(r => Math.Abs(r.Difference)),
            string.Join("; ", loanRows.Select(r => $"TK {r.AccountCode} / {r.LenderCode}: sổ vay {r.RegisterBalance} ≠ sổ cái {r.LedgerBalance}")), "loans");

        // ---- sao kê ngân hàng
        var unmatched = await (from l in db.BankStatementLines join st in db.BankStatements on l.StatementId equals st.Id
                               where st.CompanyId == ctx.CompanyId && l.TransactionDate >= periodStart && l.TransactionDate <= periodEnd && l.Status != StatementLineStatus.Matched select l.Id).CountAsync();
        Add("BANK_UNMATCHED", Sev(unmatched > 0, "Confirm"), "Dòng sao kê chưa khớp sổ", unmatched, null, unmatched > 0 ? "Đối chiếu ngân hàng là bước riêng; còn dòng chưa khớp (kể cả phí, lãi chờ xử lý)." : "", "bank");

        // ---- chuyển kỳ hợp lệ: không buộc bằng 0
        var grniAccount = (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "GRNI"))?.AccountId;
        if (grniAccount is { } gid)
        {
            var grni = -(await db.JournalLines.Where(l => l.CompanyId == ctx.CompanyId && l.AccountId == gid && l.PostingDate <= periodEnd)
                .GroupBy(l => l.Side).Select(g => new { g.Key, T = g.Sum(x => x.Amount) }).ToListAsync()).Sum(x => (int)x.Key * x.T);
            Add("GRNI_CARRYOVER", grni != 0 ? "CarryOver" : "Ok", "Hàng về chưa có hóa đơn (GRNI)", grni != 0 ? 1 : 0, grni, grni != 0 ? "Chuyển kỳ hợp lệ: chờ hóa đơn của nhà cung cấp. Không buộc bằng 0." : "", "procurement");
        }
        var undelivered = (await invoices.UninvoicedAsync(ctx, InvoiceKind.Sales, periodEnd)).Count;
        Add("DELIVERED_NOT_INVOICED", undelivered > 0 ? "CarryOver" : "Ok", "Hàng đã giao chưa lập hóa đơn", undelivered, null, undelivered > 0 ? "Chuyển kỳ hợp lệ nếu đúng điều khoản; theo dõi để lập hóa đơn." : "", "trade");
        var inTransit = await db.PaymentOrders.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId && (o.Status == PaymentOrderStatus.SentToBank || o.Status == PaymentOrderStatus.ExecutedByBank)).ToListAsync();
        Add("PAYMENTS_IN_TRANSIT", inTransit.Count > 0 ? "CarryOver" : "Ok", "Lệnh chi đã gửi ngân hàng, chưa ghi sổ", inTransit.Count, inTransit.Sum(o => o.Amount),
            inTransit.Count > 0 ? "Tiền đang đi: chuyển kỳ hợp lệ." : "", "bank");

        // ---- việc cuối kỳ chưa làm (cần xác nhận, không chặn): khấu hao, lãi vay, khấu trừ GTGT, kết chuyển — mỗi mục dẫn tới màn hình để xử lý
        var cid = ctx.CompanyId;
        if (await db.LongTermAssets.AnyAsync(a => a.CompanyId == cid && a.Status == Hak.Domain.Assets.AssetStatus.Active))
        {
            var done = await db.DepreciationRuns.AnyAsync(r => r.CompanyId == cid && r.Status == Hak.Domain.Assets.DepreciationRunStatus.Active && r.PeriodEnd >= periodEnd);
            Add("DEPRECIATION_RUN", Sev(!done, "Confirm"), "Khấu hao / phân bổ kỳ này đã chạy", done ? 0 : 1, null, done ? "" : "Còn thẻ tài sản / CCDC đang dùng nhưng chưa có lần chạy khấu hao / phân bổ phủ kỳ này.", "assets");
        }
        if (await db.LoanContracts.AnyAsync(l => l.CompanyId == cid && l.Status == Hak.Domain.Loans.LoanStatus.Active))
        {
            var done = await db.LoanInterestRuns.AnyAsync(r => r.CompanyId == cid && r.Status == Hak.Domain.Loans.LoanRunStatus.Active && r.PeriodEnd >= periodEnd);
            Add("LOAN_INTEREST_RUN", Sev(!done, "Confirm"), "Lãi vay kỳ này đã trích", done ? 0 : 1, null, done ? "" : "Còn khế ước vay đang hiệu lực nhưng chưa có lần trích lãi phủ kỳ này.", "loans");
        }
        try
        {
            var vatPreview = await vat.PreviewAsync(ctx, periodId);
            if (vatPreview.OffsetAmount > 0)
            {
                var offsetDone = await db.Vouchers.AnyAsync(v => v.CompanyId == cid && v.SourceSystem == Hak.Application.Tax.VatOffsetService.SourceSystem && v.SourceId == p.Id.ToString() && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed);
                Add("VAT_OFFSET", Sev(!offsetDone, "Confirm"), "Khấu trừ thuế GTGT cuối kỳ đã lập", offsetDone ? 0 : 1, vatPreview.OffsetAmount, offsetDone ? "" : $"Có thể khấu trừ {vatPreview.OffsetAmount} nhưng chưa có chứng từ khấu trừ của kỳ.", "vat");
            }
        }
        catch (DomainException) { /* chưa ánh xạ vai trò thuế: công ty không dùng khấu trừ GTGT tự động */ }
        // Hóa đơn điện tử phát hành từ HAK trong kỳ: còn chờ nhà cung cấp, bị từ chối, hoặc đã phát hành nhưng chưa ghi sổ được thì kỳ không đóng được (sổ và hệ thống HĐĐT sẽ lệch).
        if (await db.EInvoiceDocuments.AnyAsync(d => d.CompanyId == cid && d.InvoiceId != null))
        {
            var stuck = await db.EInvoiceDocuments.CountAsync(d => d.CompanyId == cid && d.InvoiceId != null && d.IssueDate >= periodStart && d.IssueDate <= periodEnd
                && (d.Status == Hak.Domain.Tax.EInvoiceDocStatus.Issuing || d.Status == Hak.Domain.Tax.EInvoiceDocStatus.Rejected || d.PostError != null));
            Add("EINVOICE_PENDING", Sev(stuck > 0, "Blocking"), "Hóa đơn điện tử đã xử lý xong (không chờ, không bị từ chối, đã ghi sổ)", stuck, null,
                stuck == 0 ? "" : $"{stuck} hóa đơn điện tử của kỳ còn chờ nhà cung cấp, bị từ chối hoặc đã phát hành nhưng chưa ghi sổ: xử lý ở màn Hóa đơn điện tử.", "einvoice");
        }
        // Thông báo 01/TB-RSĐT của cơ quan thuế chưa phản hồi: nhắc (không chặn cứng), quá hạn thì ghi rõ.
        var pendingRisk = await db.EInvoiceRiskNotices.AsNoTracking().Where(n => n.CompanyId == cid && n.Status == Hak.Domain.Tax.RiskNoticeStatus.Received).Select(n => n.ResponseDeadline).ToListAsync();
        if (pendingRisk.Count > 0)
        {
            var overdue = pendingRisk.Count(d => d is { } x && x < periodEnd);
            Add("EINVOICE_RISK_NOTICE", Sev(true, "Confirm"), "Thông báo 01/TB-RSĐT của cơ quan thuế đã phản hồi", pendingRisk.Count, null,
                $"{pendingRisk.Count} thông báo chưa phản hồi" + (overdue > 0 ? $", trong đó {overdue} thông báo có hạn phản hồi trước ngày cuối kỳ" : "") + ": xử lý ở màn Hóa đơn điện tử › Thông báo của cơ quan thuế.", "einvoice");
        }
        var hasClosingRules = await db.ClosingRuleSets.AnyAsync(r => r.CompanyId == cid && r.Status == Hak.Domain.Rules.RuleStatus.Active && r.EffectiveFrom <= periodEnd && (r.EffectiveTo == null || r.EffectiveTo >= periodEnd));
        if (hasClosingRules)
        {
            var key = p.Id.ToString();
            var closed = await db.Vouchers.AnyAsync(v => v.CompanyId == cid && v.SourceSystem == Hak.Application.Rules.ClosingService.SourceSystem && (v.SourceId == key || v.SourceId!.StartsWith(key + "#")) && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed);
            Add("CLOSING_VOUCHER", Sev(!closed, "Confirm"), "Chứng từ kết chuyển cuối kỳ đã lập", closed ? 0 : 1, null, closed ? "" : "Có bộ quy tắc kết chuyển đang hiệu lực nhưng kỳ chưa có chứng từ kết chuyển (làm sau khi các bút toán điều chỉnh xong).", "periods");
        }

        var blocking = items.Count(i => i.Severity == "Blocking");
        return new PeriodCloseChecklist(p.Id, p.PeriodNo, periodStart, periodEnd, p.Status.ToString(), blocking == 0 && p.Status == PeriodStatus.Open, items);
    }

    /// <summary>Khóa kỳ chỉ khi bảng kiểm không còn lỗi chặn; các mục cần xác nhận và chuyển kỳ không cản trở nhưng nằm trong nhật ký khóa kỳ của lệnh khóa.</summary>
    public async Task LockCheckedAsync(SessionContext ctx, Guid periodId)
    {
        ctx.Require(Permissions.PeriodLock);
        var list = await ForPeriodAsync(ctx, periodId);
        var blocking = list.Items.Where(i => i.Severity == "Blocking").ToList();
        if (blocking.Count > 0)
            throw new DomainException(ErrorCodes.PeriodHasPending, $"Bảng kiểm đóng kỳ còn {blocking.Count} lỗi chặn: {string.Join("; ", blocking.Select(b => b.Title))}.");
        await setup.LockPeriodAsync(ctx, periodId);
    }
}
