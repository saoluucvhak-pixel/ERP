using Hak.Application.Common;
using Hak.Application.Accounting;
using Hak.Application.Settlement;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Tax;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record DiscountInvoiceCandidate(Guid Id, Guid OriginalInvoiceId, string Number, string Series, string InvoiceNo, DateOnly InvoiceDate, decimal Amount, decimal Vat);
public sealed record DiscountInvoiceProgress(Guid Id, string Number, string Series, string InvoiceNo, string Status, decimal Amount, decimal AccruedToClear, DateOnly? ReceivedDate, bool HasLegalEvidence);
public sealed record DiscountLifecycle(Guid AgreementId, Guid RunId, bool ByVoucher, string AccountingStatus, string LegalInvoiceStatus, string TaxStatus, string SettlementStatus,
    decimal Amount, decimal PostedInvoiceAmount, decimal AccruedAmount, decimal AccrualCleared, string ValuationStatus, IReadOnlyList<DiscountInvoiceProgress> Invoices,
    IReadOnlyList<DiscountInvoiceCandidate> Candidates, string? Warning);

public sealed partial class DiscountAgreementService
{
    public async Task RecordReceiptAsync(SessionContext ctx, Guid agreementId, long expectedRowVersion, Guid invoiceId, DateOnly receivedDate)
    {
        ctx.Require(Permissions.InvoiceDocument);
        await db.InTransactionAsync(async () =>
        {
            var agreement = await LoadAsync(ctx, agreementId);
            RowVersionCheck.Require(agreement.RowVersion, expectedRowVersion, $"Thỏa thuận {agreement.Code}");
            var source = await (from r in db.DiscountAgreementRuns join l in db.DiscountAgreementRunInvoices on r.Id equals l.RunId
                                join i in db.CommercialInvoices on l.AdjustmentInvoiceId equals i.Id
                                where r.AgreementId == agreement.Id && r.CompanyId == ctx.CompanyId && r.VoidedAt == null && r.VoucherId == null
                                    && i.Id == invoiceId && i.Kind == InvoiceKind.Purchase && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted)
                                select new { RunId = r.Id, Invoice = i }).FirstOrDefaultAsync()
                ?? throw new DomainException(ErrorCodes.NotFound, "Không có hóa đơn mua quyết toán còn hiệu lực.");
            if (receivedDate < source.Invoice.InvoiceDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày nhận không được trước ngày lập hóa đơn NCC.");
            if (await db.DiscountSettlementInvoiceLinks.AnyAsync(l => l.AdjustmentInvoiceId == invoiceId))
                throw new DomainException(ErrorCodes.DuplicateSource, "Ngày nhận hóa đơn đã được ghi nhận; đối chiếu hồ sơ nếu nhập sai.");
            // Zero amount means receipt evidence for an invoice-mode run, never an additional economic offset.
            db.DiscountSettlementInvoiceLinks.Add(new DiscountSettlementInvoiceLink { Id = Ids.New(), CompanyId = ctx.CompanyId, RunId = source.RunId,
                OriginalInvoiceId = source.Invoice.AdjustsInvoiceId!.Value, AdjustmentInvoiceId = invoiceId, Amount = 0, ReceivedDate = receivedDate,
                CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId });
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.invoice_received", "discount_agreement", agreement.Id.ToString(), new { invoiceId, receivedDate });
            await db.SaveChangesAsync();
        });
    }
    private async Task<List<VoucherLineInput>> SettlementPartnerLinesAsync(SessionContext ctx, Plan plan, Guid accountId, Side side, string description)
    {
        var settlement = new SettlementService(db, audit, clock);
        var result = new List<VoucherLineInput>();
        foreach (var share in plan.Shares.Where(s => s.Discount > 0))
        {
            var targets = await (from l in db.JournalLines.AsNoTracking() join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                                 where e.VoucherId == share.Invoice.VoucherId && l.AccountId == accountId && l.Side != side && l.PartnerId == share.Invoice.PartnerId && l.ForeignCurrency == null
                                 select l).OrderBy(l => l.LineNo).ToListAsync();
            var remaining = share.Discount;
            foreach (var target in targets)
            {
                var applied = Math.Min(remaining, await settlement.OpenAmountAsync(target));
                if (applied <= 0) continue;
                result.Add(new(accountId, side, applied, share.Invoice.PartnerId, Description: description, SettleJournalLineId: target.Id));
                remaining -= applied;
            }
            if (remaining > 0) result.Add(new(accountId, side, remaining, share.Invoice.PartnerId, Description: description + " — phần còn dư chưa phân bổ"));
        }
        return result;
    }

    private async Task VerifyAccrualAccountsAsync(DiscountAgreement agreement, Guid recognitionAccount, Guid balanceAccount)
    {
        var rows = await (from a in db.DiscountAccruals.AsNoTracking() join v in db.Vouchers.AsNoTracking() on a.VoucherId equals v.Id
                          where a.AgreementId == agreement.Id && v.Status == VoucherStatus.Posted select v.Id).ToListAsync();
        var lines = await db.VoucherLines.AsNoTracking().Where(l => rows.Contains(l.VoucherId)).ToListAsync();
        if (lines.Any(l => l.PartnerId is null ? l.AccountId != recognitionAccount : l.AccountId != balanceAccount || l.PartnerId != agreement.PartnerId))
            throw new DomainException(ErrorCodes.ValidationFailed, "Tài khoản đã trích trước khác ánh xạ hiện tại; đối chiếu và xử lý đổi tài khoản trước khi quyết toán.");
    }

    public async Task<DiscountSettlementInvoiceLink> LinkInvoiceAsync(SessionContext ctx, Guid agreementId, long expectedRowVersion, Guid invoiceId, DateOnly? receivedDate)
    {
        ctx.Require(Permissions.InvoiceDocument);
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, agreementId);
            RowVersionCheck.Require(a.RowVersion, expectedRowVersion, $"Thỏa thuận {a.Code}");
            var run = await db.DiscountAgreementRuns.AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == ctx.CompanyId && r.AgreementId == a.Id && r.VoidedAt == null && r.ParentRunId == null)
                ?? throw new DomainException(ErrorCodes.NotFound, "Chưa có quyết toán còn hiệu lực.");
            if (run.VoucherId is not { } voucherId || run.RecognitionAccountId is null)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ liên kết vào quyết toán bằng chứng từ có dữ liệu nguồn và tài khoản đã lưu.");
            var voucher = await db.Vouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId && v.CompanyId == ctx.CompanyId);
            if (voucher.Status != VoucherStatus.Posted) throw new DomainException(ErrorCodes.InvalidStateTransition, "Ghi sổ chứng từ quyết toán trước khi liên kết hóa đơn.");
            var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại trong công ty.");
            if (inv.Status != InvoiceStatus.Draft || inv.Kind != a.Kind || inv.PartnerId != a.PartnerId || inv.Currency is not null
                || inv.Purpose != InvoicePurpose.Discount || inv.DiscountKind != DiscountKind.Trade || inv.AdjustsInvoiceId is null)
                throw new DomainException(ErrorCodes.ValidationFailed, "Phải chọn hóa đơn CKTM nháp, cùng chiều/đối tượng, bằng tiền hạch toán và có tham chiếu hóa đơn nguồn.");
            if (inv.PostingDate < voucher.PostingDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày ghi sổ hóa đơn không được trước chứng từ CKTM đã ghi nhận.");
            if (a.Kind == InvoiceKind.Purchase && (receivedDate is null || receivedDate < inv.InvoiceDate))
                throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn mua cần ngày nhận, không được trước ngày lập hóa đơn NCC.");
            if (await db.EInvoiceDocuments.AnyAsync(d => d.InvoiceId == inv.Id && d.Status != EInvoiceDocStatus.Rejected))
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Hóa đơn đã gửi phát hành; không thay đổi liên kết hạch toán sau lần gửi đầu tiên.");
            if (await db.DiscountSettlementInvoiceLinks.AnyAsync(l => l.AdjustmentInvoiceId == inv.Id)
                || await db.DiscountAgreementRunInvoices.AnyAsync(l => l.AdjustmentInvoiceId == inv.Id))
                throw new DomainException(ErrorCodes.DuplicateSource, "Hóa đơn đã thuộc một quyết toán chiết khấu.");
            var snapshot = ReadSnapshot(run) ?? throw new DomainException(ErrorCodes.ValidationFailed, "Quyết toán cũ thiếu bảng kê nguồn; đối chiếu hồ sơ trước khi liên kết.");
            var source = snapshot.Sources.SingleOrDefault(s => s.InvoiceId == inv.AdjustsInvoiceId)
                ?? throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn gốc không thuộc bảng kê quyết toán đã lưu.");
            var previous = await (from l in db.DiscountSettlementInvoiceLinks.AsNoTracking() join i in db.CommercialInvoices.AsNoTracking() on l.AdjustmentInvoiceId equals i.Id
                                  where l.RunId == run.Id && l.OriginalInvoiceId == source.InvoiceId && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted)
                                  select i.Id).ToListAsync();
            var previousLines = await db.CommercialInvoiceLines.AsNoTracking().Where(l => previous.Contains(l.InvoiceId)).ToListAsync();
            foreach (var group in inv.Lines.GroupBy(l => l.AdjustsLineNo))
            {
                var allowed = source.Lines.SingleOrDefault(l => l.LineNo == group.Key);
                if (allowed is null || group.Any(l => l.VatRate != allowed.VatRate)
                    || group.Sum(l => l.Amount) + previousLines.Where(l => l.AdjustsLineNo == group.Key).Sum(l => l.Amount) > allowed.Amount)
                    throw new DomainException(ErrorCodes.ValidationFailed, "Số tiền hoặc thuế suất dòng vượt phần chiết khấu đã quyết toán; kiểm tra các hóa đơn đã liên kết, kể cả nháp.");
            }
            var amount = inv.Lines.Sum(l => l.FunctionalAmount);
            if (amount <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn liên kết phải có tiền chiết khấu lớn hơn 0.");
            var link = new DiscountSettlementInvoiceLink { Id = Ids.New(), CompanyId = ctx.CompanyId, RunId = run.Id, OriginalInvoiceId = source.InvoiceId,
                AdjustmentInvoiceId = inv.Id, Amount = amount, ReceivedDate = a.Kind == InvoiceKind.Purchase ? receivedDate : null, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.DiscountSettlementInvoiceLinks.Add(link);
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.link_invoice", "discount_agreement", a.Id.ToString(), new { run.Id, invoiceId, amount, receivedDate });
            await db.SaveChangesAsync();
            return link;
        });
    }

    internal sealed record DiscountPostingOffset(decimal AlreadyRecognized, decimal AccruedToClear, Guid? RecognitionAccountId, Guid? AccrualAccountId);

    internal static async Task<DiscountPostingOffset?> InvoiceOffsetAsync(HakDbContext database, CommercialInvoice invoice)
    {
        var allocation = await database.DiscountAgreementRunInvoices.AsNoTracking().FirstOrDefaultAsync(l => l.AdjustmentInvoiceId == invoice.Id);
        var link = await database.DiscountSettlementInvoiceLinks.AsNoTracking().FirstOrDefaultAsync(l => l.AdjustmentInvoiceId == invoice.Id);
        if (allocation is null && link is null)
        {
            if (invoice.Purpose == InvoicePurpose.Increase && invoice.AdjustmentReason == InvoiceAdjustmentReason.CommercialRebate)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Thu hồi CKTM phải được tạo từ quyết toán bổ sung/thu hồi có nguồn đã duyệt.");
            if (invoice.Purpose == InvoicePurpose.Discount && invoice.DiscountKind == DiscountKind.Trade && invoice.AdjustsInvoiceId is { } originalId)
            {
                var runs = await (from r in database.DiscountAgreementRuns.AsNoTracking() join a in database.DiscountAgreements.AsNoTracking() on r.AgreementId equals a.Id
                                  join v in database.Vouchers.AsNoTracking() on r.VoucherId equals v.Id
                                  where r.CompanyId == invoice.CompanyId && r.VoidedAt == null && v.Status == VoucherStatus.Posted
                                      && a.Kind == invoice.Kind && a.PartnerId == invoice.PartnerId select r).ToListAsync();
                foreach (var pending in runs)
                {
                    var stored = ReadSnapshot(pending);
                    var sourceShare = stored?.Preview.Invoices.FirstOrDefault(i => i.InvoiceId == originalId);
                    if (sourceShare is null) continue;
                    throw new DomainException(ErrorCodes.InvalidStateTransition, "Hóa đơn nguồn có CKTM đã ghi bằng chứng từ: liên kết hóa đơn vào Tiến độ / Hóa đơn của quyết toán trước khi phát hành/ghi sổ để tránh ghi giảm hai lần. Nếu là khoản CKTM khác, quyết toán theo thỏa thuận riêng để xác định nguồn.");
                }
            }
            return null;
        }
        var runId = allocation?.RunId ?? link!.RunId;
        var run = await database.DiscountAgreementRuns.AsNoTracking().FirstAsync(r => r.Id == runId && r.CompanyId == invoice.CompanyId);
        if (run.VoidedAt is not null) throw new DomainException(ErrorCodes.InvalidStateTransition, "Quyết toán nguồn đã hủy.");
        var snapshot = ReadSnapshot(run) ?? throw new DomainException(ErrorCodes.ValidationFailed, "Quyết toán thiếu bảng kê nguồn; không tự suy phần bù trừ.");
        var source = snapshot.Sources.SingleOrDefault(s => s.InvoiceId == invoice.AdjustsInvoiceId && s.Purpose == invoice.Purpose);
        var amount = invoice.Lines.Sum(l => l.FunctionalAmount);
        var expectedAmount = allocation?.Amount ?? link!.Amount;
        var clawback = run.ParentRunId != null && expectedAmount < 0 && invoice.Purpose == InvoicePurpose.Increase && invoice.AdjustmentReason == InvoiceAdjustmentReason.CommercialRebate;
        if ((!clawback && (invoice.Purpose != InvoicePurpose.Discount || invoice.DiscountKind != DiscountKind.Trade)) || invoice.PartnerId != snapshot.Policy.PartnerId
            || invoice.Kind != snapshot.Policy.Kind || source is null || amount != Math.Abs(expectedAmount) || invoice.Currency != source.Currency
            || invoice.ExchangeRate != source.ExchangeRate || invoice.Lines.Any(l => !source.Lines.Any(s => s.LineNo == l.AdjustsLineNo && s.VatRate == l.VatRate)))
            throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn không còn khớp nguồn quyết toán đã lưu; hủy nháp sai và lập lại.");
        foreach (var line in invoice.Lines)
        {
            var functional = MoneyMath.Convert(line.Amount, invoice.ExchangeRate ?? 1m, snapshot.FunctionalDecimalPlaces);
            var vat = line.VatRate is { } rate ? MoneyMath.Round(functional * rate / 100m, snapshot.FunctionalDecimalPlaces) : 0;
            if (line.FunctionalAmount != functional || line.VatAmount != vat)
                throw new DomainException(ErrorCodes.ValidationFailed, "Tiền quy đổi/thuế dòng hóa đơn không khớp số tiền và thuế suất quyết toán.");
        }
        if (allocation is not null)
        {
            foreach (var l in source.Lines)
                if (invoice.Lines.Where(i => i.AdjustsLineNo == l.LineNo).Sum(i => i.Amount) != l.Amount)
                    throw new DomainException(ErrorCodes.ValidationFailed, "Dòng hóa đơn bị đổi so với phân bổ quyết toán.");
            if (run.ParentRunId != null)
            {
                if (invoice.Kind == InvoiceKind.Purchase) await CheckAmendmentPurchaseDestinationsAsync(database, invoice.CompanyId, [invoice.AdjustsInvoiceId!.Value]);
                if (allocation.Amount > 0 && await (from ri in database.DiscountAgreementRunInvoices join i in database.CommercialInvoices on ri.AdjustmentInvoiceId equals i.Id
                    where ri.RunId == run.Id && ri.Amount < 0 && i.Status != InvoiceStatus.Posted select i.Id).AnyAsync())
                    throw new DomainException(ErrorCodes.InvalidStateTransition, "Ghi sổ hóa đơn thu hồi của lần này trước phần bổ sung để không cấp vượt quyền lợi còn lại.");
                return new(0, 0, run.RecognitionAccountId, null); // Accrual was consumed by the original run, never clear it twice.
            }
            if (run.AccrualSettlementMode != DiscountAccrualSettlementMode.PerInvoice) return null; // Existing runs retain their original reversal workflow.
            var postedAccrual = (await (from a in database.DiscountAccruals.AsNoTracking() join v in database.Vouchers.AsNoTracking() on a.VoucherId equals v.Id
                                       where a.AgreementId == run.AgreementId && v.Status == VoucherStatus.Posted select a.Adjustment).ToListAsync()).Sum();
            if (postedAccrual != run.AccruedAmount) throw new DomainException(ErrorCodes.InvalidStateTransition, "Nguồn trích trước đã đổi sau quyết toán; đối chiếu và lập lại quyết toán chưa ghi sổ.");
            if (allocation.AccruedAmount > 0 && (run.AccrualAccountId is null || run.RecognitionAccountId is null))
                throw new DomainException(ErrorCodes.ValidationFailed, "Thiếu tài khoản trích trước đã lưu.");
            return new(0, allocation.AccruedAmount, run.RecognitionAccountId, run.AccrualAccountId);
        }
        var otherIds = await (from l in database.DiscountSettlementInvoiceLinks.AsNoTracking() join i in database.CommercialInvoices.AsNoTracking() on l.AdjustmentInvoiceId equals i.Id
                              where l.RunId == run.Id && l.OriginalInvoiceId == invoice.AdjustsInvoiceId && i.Id != invoice.Id
                                  && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted) select i.Id).ToListAsync();
        var others = await database.CommercialInvoiceLines.AsNoTracking().Where(l => otherIds.Contains(l.InvoiceId)).ToListAsync();
        foreach (var allowed in source.Lines)
            if (invoice.Lines.Where(l => l.AdjustsLineNo == allowed.LineNo).Sum(l => l.Amount) + others.Where(l => l.AdjustsLineNo == allowed.LineNo).Sum(l => l.Amount) > allowed.Amount)
                throw new DomainException(ErrorCodes.ValidationFailed, "Các hóa đơn liên kết vượt phần quyết toán của dòng nguồn.");
        var voucher = await database.Vouchers.AsNoTracking().FirstAsync(v => v.Id == run.VoucherId && v.CompanyId == run.CompanyId);
        if (voucher.Status != VoucherStatus.Posted || invoice.PostingDate < voucher.PostingDate || run.RecognitionAccountId is null)
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Chứng từ CKTM nguồn chưa ghi sổ/đã đảo hoặc ngày hóa đơn không phù hợp.");
        return new(link!.Amount, 0, run.RecognitionAccountId, null);
    }

    internal static async Task CheckSettlementVoucherAsync(HakDbContext database, Voucher voucher, List<ValidationError> errors)
    {
        if (!Guid.TryParse(voucher.SourceId, out var runId)) { errors.Add(new(ErrorCodes.ValidationFailed, "Nguồn chứng từ CKTM không hợp lệ.")); return; }
        var run = await database.DiscountAgreementRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && r.CompanyId == voucher.CompanyId);
        if (run is null || run.VoidedAt is not null || run.VoucherId != voucher.Id) { errors.Add(new(ErrorCodes.InvalidStateTransition, "Quyết toán CKTM nguồn không còn hiệu lực.")); return; }
        var snapshot = ReadSnapshot(run);
        if (snapshot?.AccountingLines is null) { errors.Add(new(ErrorCodes.ValidationFailed, "Chứng từ CKTM cũ thiếu snapshot hạch toán; đối chiếu trước khi ghi sổ.")); return; }
        var expected = snapshot.AccountingLines.GroupBy(l => (l.AccountId, l.Side, l.PartnerId, l.SettleJournalLineId)).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var actual = voucher.Lines.GroupBy(l => (l.AccountId, l.Side, l.PartnerId, l.SettleJournalLineId)).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        if (voucher.PostingDate != snapshot.VoucherPostingDate || voucher.Lines.Any(l => l.ForeignCurrency is not null)
            || expected.Count != actual.Count || expected.Any(kv => !actual.TryGetValue(kv.Key, out var amount) || amount != kv.Value))
            errors.Add(new(ErrorCodes.ValidationFailed, "Chứng từ CKTM bị đổi tài khoản/số tiền/ngày so với quyết toán đã lưu."));
    }

    internal static async Task CheckReversalAllowedAsync(HakDbContext database, Voucher voucher)
    {
        var clawbackRuns = await (from ri in database.DiscountAgreementRunInvoices join i in database.CommercialInvoices on ri.AdjustmentInvoiceId equals i.Id where i.VoucherId == voucher.Id && ri.Amount < 0 select ri.RunId).ToListAsync();
        if (await (from ri in database.DiscountAgreementRunInvoices join i in database.CommercialInvoices on ri.AdjustmentInvoiceId equals i.Id where clawbackRuns.Contains(ri.RunId) && ri.Amount > 0 && i.Status == InvoiceStatus.Posted select i.Id).AnyAsync())
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Đảo phần bổ sung cùng lần trước khi đảo phần thu hồi.");
        var sourceRuns = await database.DiscountAgreementRuns.Where(r => r.VoucherId == voucher.Id).Select(r => r.Id)
            .Concat(from ri in database.DiscountAgreementRunInvoices join i in database.CommercialInvoices on ri.AdjustmentInvoiceId equals i.Id where i.VoucherId == voucher.Id select ri.RunId)
            .Concat(from ri in database.DiscountSettlementInvoiceLinks join i in database.CommercialInvoices on ri.AdjustmentInvoiceId equals i.Id where i.VoucherId == voucher.Id select ri.RunId).ToListAsync();
        if (await database.DiscountAgreementRuns.AnyAsync(r => r.ParentRunId != null && sourceRuns.Contains(r.ParentRunId.Value) && r.VoidedAt == null))
            throw new DomainException(ErrorCodes.InvalidStateTransition, "Có quyết toán bổ sung/thu hồi phụ thuộc: đảo và hủy lần phía sau trước, không thay đổi nguồn đã chốt.");
        if (voucher.SourceSystem == VoucherSource)
        {
            var issuing = await (from l in database.DiscountSettlementInvoiceLinks join r in database.DiscountAgreementRuns on l.RunId equals r.Id
                                 join i in database.CommercialInvoices on l.AdjustmentInvoiceId equals i.Id
                                 join d in database.EInvoiceDocuments on i.Id equals d.InvoiceId
                                 where r.VoucherId == voucher.Id && i.Status == InvoiceStatus.Draft && (d.Status == EInvoiceDocStatus.Issuing || d.Status == EInvoiceDocStatus.Issued)
                                 select d.Id).AnyAsync();
            if (issuing) throw new DomainException(ErrorCodes.InvalidStateTransition, "Hóa đơn liên kết đang phát hành hoặc đã phát hành nhưng chưa ghi sổ; xử lý hóa đơn trước khi đảo chứng từ CKTM nguồn.");
            var posted = await (from l in database.DiscountSettlementInvoiceLinks join r in database.DiscountAgreementRuns on l.RunId equals r.Id
                                join i in database.CommercialInvoices on l.AdjustmentInvoiceId equals i.Id
                                where r.VoucherId == voucher.Id && i.Status == InvoiceStatus.Posted select i.Number).ToListAsync();
            if (posted.Count > 0) throw new DomainException(ErrorCodes.InvalidStateTransition, "Đảo các hóa đơn CKTM đã liên kết trước khi đảo chứng từ quyết toán: " + string.Join(", ", posted));
        }
        if (voucher.SourceSystem == AccrualSource)
        {
            var agreementId = await database.DiscountAccruals.Where(a => a.VoucherId == voucher.Id).Select(a => (Guid?)a.AgreementId).FirstOrDefaultAsync();
            if (agreementId is null) return;
            var runs = await database.DiscountAgreementRuns.Where(r => r.AgreementId == agreementId && r.VoidedAt == null).ToListAsync();
            var runIds = runs.Select(r => r.Id).ToList(); var voucherIds = runs.Where(r => r.VoucherId != null).Select(r => r.VoucherId!.Value).ToList();
            if (await database.Vouchers.AnyAsync(v => voucherIds.Contains(v.Id) && v.Status == VoucherStatus.Posted)
                || await (from l in database.DiscountAgreementRunInvoices join i in database.CommercialInvoices on l.AdjustmentInvoiceId equals i.Id
                          where runIds.Contains(l.RunId) && i.Status == InvoiceStatus.Posted select i.Id).AnyAsync())
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Trích trước đã được bù trừ trong quyết toán; đảo chứng từ/hóa đơn quyết toán trước.");
        }
    }

    public async Task<DiscountLifecycle> LifecycleAsync(SessionContext ctx, Guid agreementId, Guid? runId = null)
    {
        ctx.Require(Permissions.ReportView);
        var a = await LoadAsync(ctx, agreementId);
        var run = await db.DiscountAgreementRuns.AsNoTracking().Include(r => r.Invoices).FirstOrDefaultAsync(r => r.CompanyId == ctx.CompanyId && r.AgreementId == a.Id && (runId != null ? r.Id == runId : r.ParentRunId == null && r.VoidedAt == null))
            ?? throw new DomainException(ErrorCodes.NotFound, "Thỏa thuận chưa quyết toán.");
        var snapshot = ReadSnapshot(run);
        var links = await db.DiscountSettlementInvoiceLinks.AsNoTracking().Where(l => l.RunId == run.Id).ToListAsync();
        var ids = run.Invoices.Select(i => i.AdjustmentInvoiceId).Concat(links.Select(l => l.AdjustmentInvoiceId)).ToList();
        var invoices = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id)).OrderBy(i => i.Number).ToListAsync();
        var legalDocuments = await db.EInvoiceDocuments.AsNoTracking().Where(d => ids.Contains(d.InvoiceId ?? Guid.Empty) && d.Status == EInvoiceDocStatus.Issued).ToListAsync();
        var direction = a.Kind == InvoiceKind.Sales ? TaxDirection.Output : TaxDirection.Input;
        var evidence = await db.EInvoiceRecords.AsNoTracking().Where(e => e.CompanyId == ctx.CompanyId && e.Direction == direction && e.Status == EInvoiceStatus.Adjustment).ToListAsync();
        var statusChanges = await db.EInvoiceStatusChanges.AsNoTracking().Where(s => evidence.Select(e => e.Id).Contains(s.EInvoiceId)).ToListAsync();
        var partnerTax = (await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == a.PartnerId)).TaxCode?.Replace(" ", "");
        var ownTax = (await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId)).TaxCode?.Replace(" ", "");
        bool HasEvidence(CommercialInvoice invoice)
        {
            if (a.Kind == InvoiceKind.Sales && legalDocuments.Any(d => d.InvoiceId == invoice.Id)) return true;
            var original = snapshot?.Preview.Invoices.FirstOrDefault(i => i.InvoiceId == invoice.AdjustsInvoiceId);
            return original is not null && evidence.Any(e => e.Series == invoice.InvoiceSeries && e.Number == invoice.InvoiceNo
                && e.SellerTaxCode == (a.Kind == InvoiceKind.Sales ? ownTax : partnerTax)
                && (a.Kind != InvoiceKind.Sales || e.BuyerTaxCode is null || e.BuyerTaxCode == partnerTax)
                && e.RelatedSeries == original.InvoiceSeries && e.RelatedNumber == original.InvoiceNo
                && EInvoiceAmounts.SignedAdjustment(e) is { } signed && signed.Amount == (invoice.Purpose == InvoicePurpose.Increase ? 1 : -1) * invoice.Lines.Sum(l => l.FunctionalAmount) && signed.Vat == (invoice.Purpose == InvoicePurpose.Increase ? 1 : -1) * invoice.Lines.Sum(l => l.VatAmount)
                && (statusChanges.Where(s => s.EInvoiceId == e.Id).OrderBy(s => s.At).LastOrDefault()?.NewStatus ?? e.Status) == EInvoiceStatus.Adjustment);
        }
        var progress = invoices.Select(i => new DiscountInvoiceProgress(i.Id, i.Number, i.InvoiceSeries, i.InvoiceNo, i.Status.ToString(),
            run.Invoices.FirstOrDefault(x => x.AdjustmentInvoiceId == i.Id)?.Amount ?? links.Single(l => l.AdjustmentInvoiceId == i.Id).Amount,
            run.Invoices.FirstOrDefault(x => x.AdjustmentInvoiceId == i.Id)?.AccruedAmount ?? 0,
            links.FirstOrDefault(l => l.AdjustmentInvoiceId == i.Id)?.ReceivedDate,
            HasEvidence(i))).ToList();
        var posted = progress.Where(i => i.Status == nameof(InvoiceStatus.Posted)).Sum(i => i.Amount);
        var legal = progress.Where(i => i.Status is not (nameof(InvoiceStatus.Cancelled) or nameof(InvoiceStatus.Reversed)) && i.HasLegalEvidence).Sum(i => i.Amount);
        var voucherStatus = run.VoucherId is { } vid ? (await db.Vouchers.AsNoTracking().FirstAsync(v => v.Id == vid)).Status : (VoucherStatus?)null;
        var coverage = progress.Where(i => i.Status == nameof(InvoiceStatus.Posted)).Sum(i => Math.Abs(i.Amount));
        var requiredCoverage = snapshot?.Sources.Sum(s => s.Lines.Sum(l => MoneyMath.Convert(l.Amount, s.ExchangeRate ?? 1, snapshot.FunctionalDecimalPlaces))) ?? Math.Abs(run.Amount);
        var legalCoverage = progress.Where(i => i.Status is not (nameof(InvoiceStatus.Cancelled) or nameof(InvoiceStatus.Reversed)) && i.HasLegalEvidence).Sum(i => Math.Abs(i.Amount));
        var accounting = run.VoidedAt != null ? "Cancelled" : run.VoucherId is not null ? voucherStatus.ToString()! : coverage == requiredCoverage && progress.Count > 0 && progress.All(i => i.Status == nameof(InvoiceStatus.Posted)) ? "Posted" : coverage > 0 ? "Partial" : "Draft";
        var legalStatus = legalCoverage == requiredCoverage && progress.Count > 0 ? "Complete" : legalCoverage > 0 ? "Partial" : "Missing";
        var taxStatus = run.VoidedAt == null && coverage == requiredCoverage && legalStatus == "Complete"
            ? a.Kind == InvoiceKind.Purchase && progress.Any(i => i.Status == nameof(InvoiceStatus.Posted) && i.ReceivedDate is null) ? "PendingReceiptDate" : "ReadyForReview"
            : "PendingInvoice";
        var settlement = snapshot is null ? "LegacyReview" : accounting == "Posted" && taxStatus == "ReadyForReview" ? "ReadyForTaxReview"
            : run.VoucherId is not null && accounting == "Posted" ? posted > 0 ? "PartiallyInvoiced" : "AccountingOnly" : accounting;
        var used = await db.DiscountSettlementInvoiceLinks.Select(l => l.AdjustmentInvoiceId).Concat(db.DiscountAgreementRunInvoices.Select(l => l.AdjustmentInvoiceId)).ToListAsync();
        var sourceIds = snapshot?.Sources.Select(s => s.InvoiceId).ToList() ?? [];
        var candidates = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => i.CompanyId == ctx.CompanyId && i.Kind == a.Kind && i.PartnerId == a.PartnerId
            && i.Status == InvoiceStatus.Draft && i.Purpose == InvoicePurpose.Discount && i.DiscountKind == DiscountKind.Trade && i.AdjustsInvoiceId != null
            && sourceIds.Contains(i.AdjustsInvoiceId.Value) && !used.Contains(i.Id)).OrderBy(i => i.Number).ToListAsync();
        if (run.VoucherId is null)
            candidates = invoices.Where(i => i.Kind == InvoiceKind.Purchase && i.Status is InvoiceStatus.Draft or InvoiceStatus.Posted && !links.Any(l => l.AdjustmentInvoiceId == i.Id)).ToList();
        return new(a.Id, run.Id, run.VoucherId is not null, accounting, legalStatus, taxStatus, settlement, run.Amount, posted, run.AccruedAmount,
            run.VoucherId is not null && voucherStatus == VoucherStatus.Posted ? run.AccruedAmount : progress.Where(i => i.Status == nameof(InvoiceStatus.Posted)).Sum(i => i.AccruedToClear),
            a.Kind == InvoiceKind.Purchase && run.VoucherId is not null && posted < run.Amount ? "PendingLegalInvoiceAllocation" : "AppliedOnInvoicePosting",
            progress, candidates.Select(i => new DiscountInvoiceCandidate(i.Id, i.AdjustsInvoiceId!.Value, i.Number, i.InvoiceSeries, i.InvoiceNo, i.InvoiceDate, i.Lines.Sum(l => l.FunctionalAmount), i.Lines.Sum(l => l.VatAmount))).ToList(),
            run.AccruedAmount > 0 && run.AccrualSettlementMode == DiscountAccrualSettlementMode.LegacyReversal && run.VoucherId is null
                ? "Quyết toán cũ dùng chứng từ hoàn nhập riêng: đối chiếu toàn bộ nhóm trước khi ghi sổ." : "Trạng thái thuế sẵn sàng kiểm tra không có nghĩa đã kê khai/nộp tờ khai.");
    }
}
