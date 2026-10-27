using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hak.Application.Accounting;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record RebateDeltaLine(Guid OriginalInvoiceId, string Series, string InvoiceNo, int LineNo, string? Currency,
    decimal Previous, decimal Target, decimal Difference, decimal FunctionalDifference, decimal? VatRate);
public sealed record RebateAmendmentPreview(Guid AgreementId, long RowVersion, Guid PreviousRunId, string SourceHash,
    decimal EligibleSales, decimal PreviousAmount, decimal TargetAmount, decimal Difference, IReadOnlyList<RebateDeltaLine> Lines);
public sealed record RebateRunHistory(Guid Id, Guid? ParentRunId, int Sequence, decimal Amount, string? Reason, bool Voided, DateTime CreatedAt);

public sealed partial class DiscountAgreementService
{
    public async Task<IReadOnlyList<RebateRunHistory>> HistoryAsync(SessionContext ctx, Guid agreementId)
    {
        ctx.Require(Permissions.ReportView);
        await LoadAsync(ctx, agreementId);
        return await db.DiscountAgreementRuns.AsNoTracking().Where(r => r.AgreementId == agreementId && r.CompanyId == ctx.CompanyId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Sequence)
            .Select(r => new RebateRunHistory(r.Id, r.ParentRunId, r.Sequence, r.Amount, r.AmendmentReason, r.VoidedAt != null, r.CreatedAt)).ToListAsync();
    }

    private async Task<List<SourceAllocation>> TargetSourcesAsync(SessionContext ctx, Plan plan)
    {
        var dp = await FunctionalDpAsync(ctx.CompanyId);
        var currencies = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.DecimalPlaces);
        return plan.Shares.Select(s => new SourceAllocation(s.Invoice.Id, s.Invoice.Currency, s.Invoice.ExchangeRate,
            s.Invoice.Currency is { } c ? currencies[c] : dp, s.Lines.Select(l => {
                var original = s.Invoice.Lines.Single(x => x.LineNo == l.LineNo);
                return new SourceLineAllocation(l.LineNo, l.Amount, original.VatRate, original.FunctionalAmount);
            }).ToList())).ToList();
    }

    private async Task RequireCompletedRunAsync(SessionContext ctx, DiscountAgreementRun run, bool allowUnlinkedVoucher = false)
    {
        if (run.VoucherId is { } vid && !await db.Vouchers.AnyAsync(v => v.Id == vid && v.Status == VoucherStatus.Posted))
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Ghi sổ quyết toán trước trước khi lập bổ sung/thu hồi.");
        var snapshot = ReadSnapshot(run) ?? throw new DomainException(ErrorCodes.ValidationFailed, "Quyết toán cũ thiếu snapshot: đối chiếu hồ sơ, không tự tính lại lịch sử.");
        var ids = await db.DiscountAgreementRunInvoices.Where(l => l.RunId == run.Id).Select(l => l.AdjustmentInvoiceId)
            .Concat(db.DiscountSettlementInvoiceLinks.Where(l => l.RunId == run.Id).Select(l => l.AdjustmentInvoiceId)).Distinct().ToListAsync();
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id)).ToListAsync();
        // Quyết toán (gốc hoặc bổ sung/thu hồi) bằng chứng từ đã ghi sổ mà không có hóa đơn liên kết: chuỗi bổ sung bằng chứng từ không cần hóa đơn.
        if (allowUnlinkedVoucher && run.VoucherId is not null && invs.Count == 0) return;
        var actual = invs.Where(i => i.Status == InvoiceStatus.Posted).Sum(i => i.Lines.Sum(l => l.FunctionalAmount));
        var required = snapshot.Sources.Sum(s => s.Lines.Sum(l => MoneyMath.Convert(l.Amount, s.ExchangeRate ?? 1, snapshot.FunctionalDecimalPlaces)));
        if (invs.Count == 0 || invs.Any(i => i.Status != InvoiceStatus.Posted) || actual != required)
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Hoàn tất ghi sổ/liên kết toàn bộ hóa đơn của lần quyết toán trước; không cấp hoặc thu hồi hai lần cùng quyền lợi.");
    }

    private async Task<(DiscountAgreement Agreement, DiscountAgreementRun Previous, Plan Plan, List<SourceAllocation> Target, RebateAmendmentPreview Preview)> AmendmentPlanAsync(SessionContext ctx, Guid id, bool allowUnlinkedVoucher = false)
    {
        var a = await LoadAsync(ctx, id);
        var runs = await db.DiscountAgreementRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId && r.AgreementId == id && r.VoidedAt == null).OrderBy(r => r.Sequence).ToListAsync();
        if (runs.Count == 0) throw new DomainException(ErrorCodes.InvalidStateTransition, "Lập quyết toán gốc trước.");
        foreach (var run in runs) await RequireCompletedRunAsync(ctx, run, allowUnlinkedVoucher);
        var previous = runs[^1];
        var frozen = ReadSnapshot(previous)!;
        // Agreement service forbids editing an effective policy. Also verify against the frozen original policy.
        var policy = new AgreementInput(a.Kind, a.PartnerId, a.Code, a.Name, a.FromDate, a.ToDate, a.Tiers.OrderBy(t => t.Threshold).Select(t => new AgreementTierInput(t.Threshold, t.Percent)).ToList(), a.Progressive);
        if (JsonSerializer.Serialize(policy) != JsonSerializer.Serialize(frozen.Policy))
            throw new DomainException(ErrorCodes.ValidationFailed, "Điều khoản khác snapshot đã duyệt; không tự sửa quyền lợi của quyết toán cũ.");
        var plan = await PlanAsync(ctx, a);
        var target = await TargetSourcesAsync(ctx, plan);
        var prior = frozen.TargetSources ?? frozen.Sources;
        var dp = await FunctionalDpAsync(ctx.CompanyId);
        var ids = prior.Select(s => s.InvoiceId).Concat(target.Select(s => s.InvoiceId)).Distinct().Order().ToList();
        var originals = await db.CommercialInvoices.AsNoTracking().Where(i => ids.Contains(i.Id) && i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var deltas = new List<RebateDeltaLine>();
        foreach (var invoiceId in ids)
        {
            var before = prior.SingleOrDefault(s => s.InvoiceId == invoiceId);
            var after = target.SingleOrDefault(s => s.InvoiceId == invoiceId);
            var source = after ?? before!;
            var original = originals[invoiceId];
            if (original.Status != InvoiceStatus.Posted || original.Currency != source.Currency || original.ExchangeRate != source.ExchangeRate)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Nguồn đã đảo/thay đổi tiền tệ; đối chiếu chuỗi hóa đơn trước khi bổ sung.");
            foreach (var lineNo in (before?.Lines.Select(l => l.LineNo) ?? []).Concat(after?.Lines.Select(l => l.LineNo) ?? []).Distinct().Order())
            {
                var oldLine = before?.Lines.SingleOrDefault(l => l.LineNo == lineNo);
                var newLine = after?.Lines.SingleOrDefault(l => l.LineNo == lineNo);
                if (oldLine != null && newLine != null && oldLine.VatRate != newLine.VatRate)
                    throw new DomainException(ErrorCodes.ValidationFailed, "Thuế suất nguồn đã đổi so với snapshot.");
                var oldAmount = oldLine?.Amount ?? 0; var newAmount = newLine?.Amount ?? 0;
                var difference = newAmount - oldAmount;
                if (difference == 0) continue;
                var functional = Math.Sign(difference) * MoneyMath.Convert(Math.Abs(difference), source.ExchangeRate ?? 1, dp);
                if (functional == 0 || functional != MoneyMath.Convert(newAmount, source.ExchangeRate ?? 1, dp) - MoneyMath.Convert(oldAmount, source.ExchangeRate ?? 1, dp))
                    throw new DomainException(ErrorCodes.ValidationFailed, "Chênh lệch ngoại tệ không biểu diễn được đúng số tiền đã chốt do làm tròn; cần đối chiếu riêng.");
                deltas.Add(new(invoiceId, original.InvoiceSeries, original.InvoiceNo, lineNo, source.Currency, oldAmount, newAmount, difference, functional, (newLine ?? oldLine)!.VatRate));
            }
        }
        var previousAmount = prior.Sum(s => s.Lines.Sum(l => MoneyMath.Convert(l.Amount, s.ExchangeRate ?? 1, dp)));
        if (a.Kind == InvoiceKind.Purchase) await CheckAmendmentPurchaseDestinationsAsync(db, ctx.CompanyId, deltas.Select(d => d.OriginalInvoiceId).Distinct().ToList());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Runs = runs.Select(r => r.Id), target, deltas, plan.Eligible, plan.Percent }))));
        return (a, previous, plan, target, new(a.Id, a.RowVersion, previous.Id, hash, plan.Eligible, previousAmount, plan.Amount, deltas.Sum(d => d.FunctionalDifference), deltas));
    }

    public async Task<RebateAmendmentPreview> AmendmentPreviewAsync(SessionContext ctx, Guid id, bool byVoucher = false)
    {
        ctx.Require(Permissions.ReportView);
        return (await AmendmentPlanAsync(ctx, id, byVoucher)).Preview;
    }

    private static async Task CheckAmendmentPurchaseDestinationsAsync(Hak.Data.HakDbContext database, Guid companyId, IReadOnlyList<Guid> ids)
    {
        var stockIds = await database.CommercialInvoiceLines.Where(l => ids.Contains(l.InvoiceId) && l.StockDocumentId != null).Select(l => l.StockDocumentId!.Value).Distinct().ToListAsync();
        var assetSource = await (from source in database.AssetCostSources join line in database.JournalLines on source.JournalLineId equals line.Id
            join entry in database.JournalEntries on line.JournalEntryId equals entry.Id join invoice in database.CommercialInvoices on entry.VoucherId equals invoice.VoucherId
            where source.CompanyId == companyId && ids.Contains(invoice.Id) select source.Id).AnyAsync();
        if (assetSource || await database.CommercialInvoiceLines.AnyAsync(l => ids.Contains(l.InvoiceId) && l.AccountId != null && database.LongTermAssets.Any(a => a.CompanyId == companyId && a.AssetAccountId == l.AccountId)))
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Nguồn mua đã vốn hóa tài sản/CCDC: cần quy trình điều chỉnh nguyên giá riêng; không tự đưa CKTM vào giá vốn.");
        if (stockIds.Count == 0) return;
        var receipts = await database.StockDocuments.Where(d => stockIds.Contains(d.Id)).Select(d => new { d.PostingDate, d.WarehouseId }).ToListAsync();
        var first = receipts.Min(d => d.PostingDate); var warehouses = receipts.Select(d => d.WarehouseId).ToList();
        var items = await database.StockDocumentLines.Where(l => stockIds.Contains(l.DocumentId)).Select(l => l.ItemId).Distinct().ToListAsync();
        var production = await (from issue in database.ProductionOrderIssues join order in database.ProductionOrders on issue.OrderId equals order.Id
            join doc in database.StockDocuments on issue.StockDocumentId equals doc.Id join line in database.StockDocumentLines on doc.Id equals line.DocumentId
            where order.CompanyId == companyId && order.Status != Hak.Domain.Production.ProductionOrderStatus.Cancelled
                && doc.Status == Hak.Domain.Inventory.StockDocumentStatus.Posted && doc.PostingDate >= first && warehouses.Contains(doc.WarehouseId) && items.Contains(line.ItemId)
            select issue.Id).AnyAsync();
        var repair = await (from material in database.AssetRepairMaterials join doc in database.StockDocuments on material.StockDocumentId equals doc.Id
            where material.CompanyId == companyId && doc.Status == Hak.Domain.Inventory.StockDocumentStatus.Posted && doc.PostingDate >= first
                && warehouses.Contains(doc.WarehouseId) && items.Contains(material.ItemId) select material.Id).AnyAsync();
        if (production || repair) throw new DomainException(ErrorCodes.InvalidStateTransition, "Vật tư nguồn đã dùng cho sản xuất/sửa chữa tài sản: cần phân bổ lại dở dang, giá thành hoặc tài sản riêng; không tự dồn chênh lệch CKTM vào giá vốn.");
    }

    public async Task<DiscountAgreementRun> AmendAsync(SessionContext ctx, Guid id, long rowVersion, Guid previousRunId, string sourceHash,
        string reason, string series, string firstNo, DateOnly invoiceDate, DateOnly postingDate)
    {
        ctx.Require(Permissions.InvoiceDocument); // Uses the already approved frozen policy; draft invoices still require the normal independent posting authority.
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(series) || string.IsNullOrWhiteSpace(firstNo))
            throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do, ký hiệu và số hóa đơn đầu tiên.");
        return await db.InTransactionAsync(async () => {
            var p = await AmendmentPlanAsync(ctx, id);
            RowVersionCheck.Require(p.Agreement.RowVersion, rowVersion, "Thỏa thuận");
            if (p.Previous.Id != previousRunId || p.Preview.SourceHash != sourceHash)
                throw new DomainException(ErrorCodes.ConcurrencyConflict, "Nguồn/chuỗi quyết toán đã đổi; tính lại và duyệt số chênh lệch mới.");
            if (!p.Agreement.IsActive || invoiceDate < p.Agreement.ToDate || postingDate < p.Agreement.ToDate)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Thỏa thuận phải đang áp dụng và ngày điều chỉnh không được trước cuối kỳ.");
            var historyIds = await db.DiscountAgreementRuns.Where(r => r.AgreementId == id && r.CompanyId == ctx.CompanyId).Select(r => r.Id).ToListAsync();
            var historicalInvoiceIds = await db.DiscountAgreementRunInvoices.Where(r => historyIds.Contains(r.RunId)).Select(r => r.AdjustmentInvoiceId)
                .Concat(db.DiscountSettlementInvoiceLinks.Where(r => historyIds.Contains(r.RunId)).Select(r => r.AdjustmentInvoiceId)).Distinct().ToListAsync();
            var historicalInvoices = await db.CommercialInvoices.AsNoTracking().Where(i => historicalInvoiceIds.Contains(i.Id) && (i.Status == InvoiceStatus.Posted || i.Status == InvoiceStatus.Reversed)).ToListAsync();
            var historicalVouchers = historicalInvoices.Where(i => i.VoucherId != null).Select(i => i.VoucherId!.Value)
                .Concat(await db.DiscountAgreementRuns.Where(r => historyIds.Contains(r.Id) && r.VoucherId != null).Select(r => r.VoucherId!.Value).ToListAsync()).ToList();
            var latestPosting = await db.Vouchers.Where(v => v.CompanyId == ctx.CompanyId && (historicalVouchers.Contains(v.Id) || (v.ReversesVoucherId != null && historicalVouchers.Contains(v.ReversesVoucherId.Value)))
                && (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed)).MaxAsync(v => (DateOnly?)v.PostingDate);
            var latestInvoice = historicalInvoices.Count > 0 ? historicalInvoices.Max(i => i.InvoiceDate) : p.Agreement.ToDate;
            if ((latestPosting is { } minPosting && postingDate < minPosting) || invoiceDate < latestInvoice)
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Ngày bổ sung/thu hồi không được trước hóa đơn/quyết toán hoặc lần đảo đã ghi nhận (hạch toán từ {latestPosting?.Vn()}, hóa đơn từ {latestInvoice.Vn()}); không ghi quyền lợi hai lần trong khoảng trước ngày đảo.");
            if (p.Preview.Lines.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không có chênh lệch quyền lợi theo từng dòng; không tạo chứng từ.");
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == ctx.CompanyId && x.StartDate <= postingDate && x.EndDate >= postingDate);
            if (period?.Status != Domain.Organization.PeriodStatus.Open) throw new DomainException(ErrorCodes.PeriodLocked, "Kỳ hạch toán bổ sung/thu hồi không mở.");
            var ra = await RebateAccountsAsync(ctx.CompanyId, p.Agreement.Kind, false, false);
            var root = await db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.AgreementId == id && r.VoidedAt == null && r.ParentRunId == null);
            var recognition = root.RecognitionAccountId ?? ra.Discount;
            if (p.Agreement.Kind == InvoiceKind.Sales && root.RecognitionAccountId is null)
            {
                var voucherIds = await (from l in db.DiscountAgreementRunInvoices join i in db.CommercialInvoices on l.AdjustmentInvoiceId equals i.Id where l.RunId == root.Id select i.VoucherId).ToListAsync();
                var recognized = await (from l in db.JournalLines join e in db.JournalEntries on l.JournalEntryId equals e.Id where voucherIds.Contains(e.VoucherId) && l.AccountId == recognition select new { l.Side, l.Amount }).ToListAsync();
                if (recognized.Sum(l => (int)l.Side * l.Amount) != root.Amount)
                    throw new DomainException(ErrorCodes.ValidationFailed, "Tài khoản CKTM gốc khác cấu hình hiện tại; đối chiếu trước khi bổ sung/thu hồi.");
            }
            var sequence = await db.DiscountAgreementRuns.Where(r => r.AgreementId == id).MaxAsync(r => r.Sequence) + 1;
            var run = new DiscountAgreementRun { Id = Ids.New(), AgreementId = id, CompanyId = ctx.CompanyId, ParentRunId = previousRunId, Sequence = sequence,
                AmendmentReason = reason.Trim(), EligibleSales = p.Plan.Eligible, Percent = p.Plan.Percent, Amount = p.Preview.Difference,
                RecognitionAccountId = recognition, AccrualSettlementMode = DiscountAccrualSettlementMode.PerInvoice, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.DiscountAgreementRuns.Add(run);
            var sources = new List<SourceAllocation>(); var statementRows = new List<AgreementStatementRow>(); var previewRows = new List<AgreementInvoiceShare>();
            var n = 0;
            // A net-zero run can still redistribute rights between sources/tax rates: keep both directions.
            foreach (var group in p.Preview.Lines.GroupBy(l => (l.OriginalInvoiceId, Increase: l.Difference < 0)).OrderByDescending(g => g.Key.Increase))
            {
                var rows = group.ToList(); var original = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == group.Key.OriginalInvoiceId);
                var purpose = group.Key.Increase ? InvoicePurpose.Increase : InvoicePurpose.Discount;
                var invoice = await invoices.CreateAdjustmentAsync(ctx, new(original.Id, purpose, series.Trim(), InvoiceNoAt(firstNo.Trim(), n++), invoiceDate, postingDate,
                    $"{(group.Key.Increase ? "Thu hồi" : "Bổ sung")} CKTM {p.Agreement.Code}, lần {sequence}: {reason.Trim()}", rows.Select(l => new AdjustmentLineInput(l.LineNo, Amount: Math.Abs(l.Difference))).ToList(), DiscountKind.Trade, UnderAgreement: true));
                var amount = rows.Sum(l => l.FunctionalDifference);
                if (invoice.Lines.Sum(l => l.FunctionalAmount) != Math.Abs(amount)) throw new DomainException(ErrorCodes.ValidationFailed, "Tiền hóa đơn không khớp chênh lệch đã duyệt.");
                db.DiscountAgreementRunInvoices.Add(new() { Id = Ids.New(), RunId = run.Id, CompanyId = ctx.CompanyId, OriginalInvoiceId = original.Id, AdjustmentInvoiceId = invoice.Id, Amount = amount });
                await db.SaveChangesAsync();
                var dp = p.Target.FirstOrDefault(s => s.InvoiceId == original.Id)?.DecimalPlaces ?? (ReadSnapshot(p.Previous)!.TargetSources ?? ReadSnapshot(p.Previous)!.Sources).Single(s => s.InvoiceId == original.Id).DecimalPlaces;
                sources.Add(new(original.Id, original.Currency, original.ExchangeRate, dp, rows.Select(l => new SourceLineAllocation(l.LineNo, Math.Abs(l.Difference), l.VatRate, original.Lines.Single(o => o.LineNo == l.LineNo).FunctionalAmount)).ToList(), purpose));
                var sourceSales = p.Plan.Shares.FirstOrDefault(s => s.Invoice.Id == original.Id).Sales;
                var rowSales = previewRows.Any(r => r.InvoiceId == original.Id) ? 0 : sourceSales;
                statementRows.Add(new(n, original.InvoiceSeries, original.InvoiceNo, original.InvoiceDate, rowSales, amount, invoice.InvoiceSeries, invoice.InvoiceNo, invoice.InvoiceDate, Math.Sign(amount) * invoice.Lines.Sum(l => l.VatAmount)));
                previewRows.Add(new(original.Id, original.Number, original.InvoiceSeries, original.InvoiceNo, original.PostingDate, rowSales, amount));
            }
            await db.SaveChangesAsync();
            var old = ReadSnapshot(p.Previous)!;
            run.SettlementSnapshot = JsonSerializer.Serialize(new SettlementSnapshot(3, old.FunctionalDecimalPlaces,
                old.Statement with { TotalSales = statementRows.Sum(r => r.Sales), TotalDiscount = run.Amount, Percent = p.Plan.Percent, Rows = statementRows, Mode = "Bổ sung / thu hồi CKTM",
                    Warning = "Bảng kê chỉ gồm nguồn có chênh lệch; doanh số tiền hạch toán của mỗi hóa đơn ghi một lần. Chiết khấu/thuế phân bổ mang dấu dương khi bổ sung, âm khi thu hồi; không phải dấu tăng/giảm thuế trên tờ khai." },
                new(id, p.Plan.Eligible, p.Plan.Percent, run.Amount, null, null, true, previewRows), old.Policy, sources, TargetSources: p.Target));
            var agreement = await db.DiscountAgreements.SingleAsync(a => a.Id == id); agreement.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.amend", "discount_agreement", id.ToString(), new { run.Id, previousRunId, sourceHash, reason, run.Amount, sequence });
            await db.SaveChangesAsync(); return run;
        });
    }
    /// <summary>
    /// Bổ sung / thu hồi CKTM bằng HẠCH TOÁN ĐIỀU CHỈNH (chuỗi quyết toán gốc bằng chứng từ, chưa có hóa đơn điều chỉnh). Chị chốt 27/10/2026:
    /// (1) không đổi thuế GTGT — chiết khấu phải giảm thuế đầu ra thì quyết toán bằng hóa đơn; (2) ngày ghi sổ do người lập chọn, không trước cuối kỳ thỏa thuận / lần trước, kỳ phải còn mở;
    /// (3) thu hồi không vượt quyền lợi đã chốt (lũy kế không âm). Bổ sung: Nợ giảm trừ doanh thu / Có phải thu (bù trừ công nợ hóa đơn gốc); thu hồi: Nợ phải thu / Có TK chiết khấu, không ghi vào doanh thu.
    /// Run gốc và snapshot giữ nguyên; chứng từ nháp, người khác duyệt và ghi sổ. Chỉ hóa đơn tiền hạch toán.
    /// </summary>
    public async Task<DiscountAgreementRun> AmendByVoucherAsync(SessionContext ctx, Guid id, long rowVersion, Guid previousRunId, string sourceHash, string reason, DateOnly postingDate)
    {
        ctx.Require(Permissions.VoucherCreate);
        if (engine is null) throw new InvalidOperationException("Thiếu AccountingEngine.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do bổ sung / thu hồi.");
        return await db.InTransactionAsync(async () =>
        {
            var p = await AmendmentPlanAsync(ctx, id, true);
            RowVersionCheck.Require(p.Agreement.RowVersion, rowVersion, "Thỏa thuận");
            if (p.Previous.Id != previousRunId || p.Preview.SourceHash != sourceHash)
                throw new DomainException(ErrorCodes.ConcurrencyConflict, "Nguồn/chuỗi quyết toán đã đổi; tính lại và duyệt số chênh lệch mới.");
            if (!p.Agreement.IsActive || postingDate < p.Agreement.ToDate)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Thỏa thuận phải đang áp dụng và ngày hạch toán không được trước cuối kỳ.");
            var root = await db.DiscountAgreementRuns.AsNoTracking().FirstOrDefaultAsync(r => r.AgreementId == id && r.CompanyId == ctx.CompanyId && r.VoidedAt == null && r.ParentRunId == null)
                       ?? throw new DomainException(ErrorCodes.InvalidStateTransition, "Lập quyết toán gốc trước.");
            if (root.VoucherId is null) throw new DomainException(ErrorCodes.InvalidStateTransition, "Quyết toán gốc bằng hóa đơn điều chỉnh: bổ sung / thu hồi cũng bằng hóa đơn điều chỉnh (không trộn hai chế độ).");
            if (p.Preview.Lines.Any(l => l.Currency is not null))
                throw new DomainException(ErrorCodes.ValidationFailed, "Có nguồn ngoại tệ: bổ sung / thu hồi bằng hóa đơn điều chỉnh (hạch toán điều chỉnh chỉ dùng cho hóa đơn tiền hạch toán).");
            if (p.Preview.Lines.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không có chênh lệch quyền lợi; không tạo chứng từ.");
            if (p.Preview.PreviousAmount + p.Preview.Difference < 0) throw new DomainException(ErrorCodes.ValidationFailed, "Thu hồi vượt quyền lợi đã chốt.");
            var historyIds = await db.DiscountAgreementRuns.Where(r => r.AgreementId == id && r.CompanyId == ctx.CompanyId).Select(r => r.Id).ToListAsync();
            var historyVouchers = await db.DiscountAgreementRuns.Where(r => historyIds.Contains(r.Id) && r.VoucherId != null).Select(r => r.VoucherId!.Value).ToListAsync();
            var latestPosting = await db.Vouchers.Where(v => v.CompanyId == ctx.CompanyId && (historyVouchers.Contains(v.Id) || (v.ReversesVoucherId != null && historyVouchers.Contains(v.ReversesVoucherId.Value)))
                && (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed)).MaxAsync(v => (DateOnly?)v.PostingDate);
            if (latestPosting is { } earliest && postingDate < earliest)
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Ngày hạch toán không được trước chứng từ quyết toán / lần đảo đã ghi nhận ({earliest.Vn()}).");
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == ctx.CompanyId && x.StartDate <= postingDate && x.EndDate >= postingDate);
            if (period?.Status != Domain.Organization.PeriodStatus.Open) throw new DomainException(ErrorCodes.PeriodLocked, "Kỳ hạch toán bổ sung/thu hồi không mở.");

            var ra = await RebateAccountsAsync(ctx.CompanyId, p.Agreement.Kind, false, true);
            var recognition = root.RecognitionAccountId ?? ra.Discount;
            var sequence = await db.DiscountAgreementRuns.Where(r => r.AgreementId == id).MaxAsync(r => r.Sequence) + 1;
            var run = new DiscountAgreementRun { Id = Ids.New(), AgreementId = id, CompanyId = ctx.CompanyId, ParentRunId = previousRunId, Sequence = sequence,
                AmendmentReason = reason.Trim(), EligibleSales = p.Plan.Eligible, Percent = p.Plan.Percent, Amount = p.Preview.Difference,
                RecognitionAccountId = recognition, AccrualSettlementMode = DiscountAccrualSettlementMode.PerInvoice, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            var perInvoice = p.Preview.Lines.GroupBy(l => l.OriginalInvoiceId).Select(g => (InvoiceId: g.Key, Delta: g.Sum(l => l.FunctionalDifference))).Where(x => x.Delta != 0).ToList();
            var supplement = perInvoice.Where(x => x.Delta > 0).Sum(x => x.Delta);
            var clawback = -perInvoice.Where(x => x.Delta < 0).Sum(x => x.Delta);
            var desc = $"{(clawback > 0 && supplement == 0 ? "Thu hồi" : supplement > 0 && clawback == 0 ? "Bổ sung" : "Điều chỉnh")} CKTM {p.Agreement.Code}, lần {sequence}: {reason.Trim()} — hạch toán điều chỉnh, không đổi thuế GTGT";
            var lines = new List<VoucherLineInput>();
            if (supplement > 0)
            {
                lines.Add(new(recognition, ra.DSide, supplement, Description: desc));
                foreach (var item in perInvoice.Where(x => x.Delta > 0)) lines.AddRange(await SettleOriginalLinesAsync(item.InvoiceId, item.Delta, ra.OpenItem!.Value, ra.BSide, desc));
            }
            if (clawback > 0)
            {
                lines.Add(new(recognition, Opp(ra.DSide), clawback, Description: desc));     // thu hồi ghi vào TK chiết khấu, không ghi vào doanh thu
                lines.Add(new(ra.OpenItem!.Value, Opp(ra.BSide), clawback, p.Agreement.PartnerId, Description: desc));
            }
            var voucher = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, postingDate, postingDate, desc, lines, SourceSystem: VoucherSource, SourceId: run.Id.ToString()));
            run.VoucherId = voucher.Id;
            db.DiscountAgreementRuns.Add(run);
            await db.SaveChangesAsync();

            var invoiceIds = perInvoice.Select(x => x.InvoiceId).ToList();
            var originals = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            var sources = p.Preview.Lines.GroupBy(l => (l.OriginalInvoiceId, Increase: l.Difference < 0)).Select(g =>
            {
                var original = originals[g.Key.OriginalInvoiceId];
                return new SourceAllocation(original.Id, original.Currency, original.ExchangeRate, p.Target.FirstOrDefault(t => t.InvoiceId == original.Id)?.DecimalPlaces ?? 0,
                    g.Select(l => new SourceLineAllocation(l.LineNo, Math.Abs(l.Difference), l.VatRate, original.Lines.Single(o => o.LineNo == l.LineNo).FunctionalAmount)).ToList(),
                    g.Key.Increase ? InvoicePurpose.Increase : InvoicePurpose.Discount);
            }).ToList();
            var rowNo = 0;
            var statementRows = perInvoice.Select(x => { var o = originals[x.InvoiceId];
                return new AgreementStatementRow(++rowNo, o.InvoiceSeries, o.InvoiceNo, o.InvoiceDate, p.Plan.Shares.FirstOrDefault(s => s.Invoice.Id == o.Id).Sales, x.Delta, null, null, null, 0m); }).ToList();
            var previewRows = perInvoice.Select(x => { var o = originals[x.InvoiceId];
                return new AgreementInvoiceShare(o.Id, o.Number, o.InvoiceSeries, o.InvoiceNo, o.PostingDate, p.Plan.Shares.FirstOrDefault(s => s.Invoice.Id == o.Id).Sales, x.Delta); }).ToList();
            var old = ReadSnapshot(p.Previous)!;
            var stored = await db.Vouchers.AsNoTracking().Include(v => v.Lines).FirstAsync(v => v.Id == voucher.Id);
            run.SettlementSnapshot = JsonSerializer.Serialize(new SettlementSnapshot(3, old.FunctionalDecimalPlaces,
                old.Statement with { TotalSales = statementRows.Sum(r => r.Sales), TotalDiscount = run.Amount, Percent = p.Plan.Percent, Rows = statementRows, Mode = "Bổ sung / thu hồi CKTM bằng hạch toán điều chỉnh",
                    Warning = "Bổ sung / thu hồi bằng chứng từ hạch toán: không có hóa đơn điều chỉnh và không đổi thuế GTGT [CẦN XÁC MINH cách xử lý thuế]; dấu dương là bổ sung, âm là thu hồi." },
                new(id, p.Plan.Eligible, p.Plan.Percent, run.Amount, null, null, true, previewRows), old.Policy, sources,
                stored.PostingDate, stored.Lines.Select(l => new FrozenAccountingLine(l.AccountId, l.Side, l.Amount, l.PartnerId, l.SettleJournalLineId)).ToList(), p.Target));
            var agreement = await db.DiscountAgreements.SingleAsync(a => a.Id == id); agreement.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.amend_voucher", "discount_agreement", id.ToString(), new { run.Id, previousRunId, sourceHash, reason, run.Amount, sequence, voucher = voucher.Number });
            await db.SaveChangesAsync();
            return run;
        });
    }

    /// <summary>Dòng công nợ của chứng từ bổ sung: bù trừ công nợ còn mở của hóa đơn gốc, phần dư để chưa phân bổ.</summary>
    private async Task<List<VoucherLineInput>> SettleOriginalLinesAsync(Guid originalInvoiceId, decimal amount, Guid accountId, Side side, string description)
    {
        var settlement = new SettlementService(db, audit, clock);
        var result = new List<VoucherLineInput>();
        var original = await db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == originalInvoiceId);
        var targets = await (from l in db.JournalLines.AsNoTracking() join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                             where e.VoucherId == original.VoucherId && l.AccountId == accountId && l.Side != side && l.PartnerId == original.PartnerId && l.ForeignCurrency == null
                             select l).OrderBy(l => l.LineNo).ToListAsync();
        var remaining = amount;
        foreach (var target in targets)
        {
            var applied = Math.Min(remaining, await settlement.OpenAmountAsync(target));
            if (applied <= 0) continue;
            result.Add(new(accountId, side, applied, original.PartnerId, Description: description, SettleJournalLineId: target.Id));
            remaining -= applied;
        }
        if (remaining > 0) result.Add(new(accountId, side, remaining, original.PartnerId, Description: description + " — phần còn dư chưa phân bổ"));
        return result;
    }
}
