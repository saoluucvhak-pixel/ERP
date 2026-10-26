using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hak.Application.Common;
using Hak.Application.Security;
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

    private async Task RequireCompletedRunAsync(SessionContext ctx, DiscountAgreementRun run)
    {
        if (run.VoucherId is { } vid && !await db.Vouchers.AnyAsync(v => v.Id == vid && v.Status == VoucherStatus.Posted))
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Ghi sổ quyết toán trước trước khi lập bổ sung/thu hồi.");
        var snapshot = ReadSnapshot(run) ?? throw new DomainException(ErrorCodes.ValidationFailed, "Quyết toán cũ thiếu snapshot: đối chiếu hồ sơ, không tự tính lại lịch sử.");
        var ids = await db.DiscountAgreementRunInvoices.Where(l => l.RunId == run.Id).Select(l => l.AdjustmentInvoiceId)
            .Concat(db.DiscountSettlementInvoiceLinks.Where(l => l.RunId == run.Id).Select(l => l.AdjustmentInvoiceId)).Distinct().ToListAsync();
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id)).ToListAsync();
        var actual = invs.Where(i => i.Status == InvoiceStatus.Posted).Sum(i => i.Lines.Sum(l => l.FunctionalAmount));
        var required = snapshot.Sources.Sum(s => s.Lines.Sum(l => MoneyMath.Convert(l.Amount, s.ExchangeRate ?? 1, snapshot.FunctionalDecimalPlaces)));
        if (invs.Count == 0 || invs.Any(i => i.Status != InvoiceStatus.Posted) || actual != required)
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Hoàn tất ghi sổ/liên kết toàn bộ hóa đơn của lần quyết toán trước; không cấp hoặc thu hồi hai lần cùng quyền lợi.");
    }

    private async Task<(DiscountAgreement Agreement, DiscountAgreementRun Previous, Plan Plan, List<SourceAllocation> Target, RebateAmendmentPreview Preview)> AmendmentPlanAsync(SessionContext ctx, Guid id)
    {
        var a = await LoadAsync(ctx, id);
        var runs = await db.DiscountAgreementRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId && r.AgreementId == id && r.VoidedAt == null).OrderBy(r => r.Sequence).ToListAsync();
        if (runs.Count == 0) throw new DomainException(ErrorCodes.InvalidStateTransition, "Lập quyết toán gốc trước.");
        foreach (var run in runs) await RequireCompletedRunAsync(ctx, run);
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

    public async Task<RebateAmendmentPreview> AmendmentPreviewAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        return (await AmendmentPlanAsync(ctx, id)).Preview;
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
}
