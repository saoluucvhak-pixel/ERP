using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record AdjustmentRequestLineInput(int OriginalLineNo, decimal? Quantity, decimal? Amount, string? Note = null);
public sealed record AdjustmentRequestInput(Guid InvoiceId, AdjustmentKind Kind, string Reason, IReadOnlyList<AdjustmentRequestLineInput> Lines);
public sealed record AdjustmentRequestLineView(int OriginalLineNo, string ItemCode, string ItemName, decimal InvoicedQty, decimal InvoicedAmount, decimal? Quantity, decimal? Amount, decimal Used, string? Note);
public sealed record AdjustmentRequestRow(Guid Id, string Number, Guid InvoiceId, string InvoiceNumber, string PartnerCode, string PartnerName, AdjustmentKind Kind, string Reason, ApprovalStatus Status,
    string? DecisionNote, DateTime CreatedAt, decimal TotalAmount, decimal TotalQuantity, IReadOnlyList<AdjustmentRequestLineView> Lines);

public sealed record WriteOffCandidate(Guid JournalLineId, string VoucherNumber, DateOnly PostingDate, DateOnly? DueDate, decimal Open, int OverdueDays, string? Description);
public sealed record WriteOffItemInput(Guid JournalLineId, decimal Amount, decimal ProvisionUsed = 0);
public sealed record WriteOffInput(Guid PartnerId, string Reason, string Basis, IReadOnlyList<WriteOffItemInput> Items);
public sealed record WriteOffItemView(Guid JournalLineId, string VoucherNumber, DateOnly PostingDate, decimal Amount, decimal ProvisionUsed);
public sealed record WriteOffRow(Guid Id, string Number, Guid PartnerId, string PartnerCode, string PartnerName, string Reason, string Basis, ApprovalStatus Status, string? DecisionNote,
    string? VoucherNumber, bool VoucherPending, bool Done, decimal Total, decimal Provision, DateTime CreatedAt, IReadOnlyList<WriteOffItemView> Items);

/// <summary>
/// Phê duyệt trong chu trình doanh thu. (1) Yêu cầu trả hàng / giảm giá hàng bán: người lập nêu lý do và số lượng / số tiền từng dòng hóa đơn gốc; người có thẩm quyền khác người lập duyệt;
/// khi công ty bật chính sách, InvoiceService chỉ cho nhận hàng trả lại / lập hóa đơn giảm giá trong phạm vi tổng đã duyệt của dòng đó (phần đã dùng lấy từ chính các chứng từ trả hàng,
/// hóa đơn điều chỉnh — không có sổ trừ lùi riêng). (2) Xóa nợ phải thu khó đòi: chọn khoản đang mở của một khách, căn cứ, duyệt bởi người có thẩm quyền (quyền credit.override),
/// rồi chứng từ nháp bù trừ khoản phải thu qua vai trò BAD_DEBT_PROVISION / BAD_DEBT_EXPENSE. Không theo dõi nợ đã xóa ngoài bảng cân đối [CẦN XÁC MINH theo chế độ kế toán].
/// </summary>
public sealed class SalesApprovalService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string Source = "HAK.WRITEOFF";

    // ------------------------------------------------------------------ chính sách

    public async Task SetPolicyAsync(SessionContext ctx, bool require)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            var c = await db.Companies.FirstAsync(x => x.Id == ctx.CompanyId);
            if (c.RequireSalesAdjustmentApproval == require) return;
            c.RequireSalesAdjustmentApproval = require; c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "sales.adjustment_approval_policy", "company", c.Id.ToString(), new { require }, c.RowVersion);
            await db.SaveChangesAsync();
        });
    }

    public async Task<bool> PolicyAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.VoucherView);
        return await db.Companies.AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.RequireSalesAdjustmentApproval).FirstAsync();
    }

    // ------------------------------------------------------------------ trả hàng / giảm giá

    public async Task<AdjustmentRequest> RequestAsync(SessionContext ctx, AdjustmentRequestInput input)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do trả hàng / giảm giá.");
        if (input.Lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Chưa có dòng nào.");
        if (input.Lines.Select(l => l.OriginalLineNo).Distinct().Count() != input.Lines.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi dòng hóa đơn chỉ một lần trong yêu cầu.");
        return await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == input.InvoiceId && i.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (inv.Kind != InvoiceKind.Sales || inv.Purpose != InvoicePurpose.Normal || inv.Status != InvoiceStatus.Posted)
                throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ yêu cầu trả hàng / giảm giá cho hóa đơn bán thường đã ghi sổ.");
            foreach (var l in input.Lines)
            {
                var line = inv.Lines.FirstOrDefault(x => x.LineNo == l.OriginalLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Hóa đơn không có dòng {l.OriginalLineNo}.");
                if (input.Kind == AdjustmentKind.Return)
                {
                    if (line.StockDocumentId is null) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {l.OriginalLineNo} không gắn phiếu xuất; dùng yêu cầu giảm giá.");
                    if (l.Quantity is not { } q || q <= 0 || q > (line.StockQuantity ?? 0)) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {l.OriginalLineNo}: số lượng trả phải lớn hơn 0 và không vượt số đã bán {line.StockQuantity}.");
                }
                else if (l.Amount is not { } a || a <= 0 || a > line.FunctionalAmount)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {l.OriginalLineNo}: số tiền giảm phải lớn hơn 0 và không vượt giá trị dòng {line.FunctionalAmount}.");
            }
            var r = new AdjustmentRequest { Id = Ids.New(), CompanyId = ctx.CompanyId, InvoiceId = inv.Id, Kind = input.Kind, Reason = input.Reason.Trim(), CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow };
            r.Number = await NextNumberAsync(ctx.CompanyId, DateOnly.FromDateTime(clock.UtcNow), input.Kind == AdjustmentKind.Return ? "DTH" : "DGG");
            foreach (var l in input.Lines)
                r.Lines.Add(new AdjustmentRequestLine { Id = Ids.New(), RequestId = r.Id, OriginalLineNo = l.OriginalLineNo, Quantity = input.Kind == AdjustmentKind.Return ? l.Quantity : null, Amount = input.Kind == AdjustmentKind.Discount ? l.Amount : null, Note = l.Note });
            db.AdjustmentRequests.Add(r);
            await audit.AppendAsync(ctx.CompanyId, ctx, "sales.adjustment_request", "adjustment_request", r.Id.ToString(), new { r.Number, inv = inv.Number, r.Kind, lines = input.Lines.Count });
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<AdjustmentRequest> DecideAsync(SessionContext ctx, Guid id, bool approve, string? note)
    {
        ctx.Require(Permissions.VoucherApprove);
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối phải ghi lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var r = await db.AdjustmentRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Yêu cầu không tồn tại.");
            if (r.Status != ApprovalStatus.Requested) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Yêu cầu {r.Number} không ở trạng thái chờ duyệt.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && r.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt phải khác người lập yêu cầu.");
            r.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected; r.DecisionNote = note; r.DecidedBy = ctx.UserId; r.DecidedAt = clock.UtcNow;
            await audit.AppendAsync(ctx.CompanyId, ctx, approve ? "sales.adjustment_approve" : "sales.adjustment_reject", "adjustment_request", r.Id.ToString(), new { r.Number, note });
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var r = await db.AdjustmentRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Yêu cầu không tồn tại.");
            if (r.Status is not (ApprovalStatus.Requested or ApprovalStatus.Approved)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Yêu cầu {r.Number} đã {(r.Status == ApprovalStatus.Rejected ? "bị từ chối" : "hủy")}.");
            r.Status = ApprovalStatus.Cancelled; r.DecisionNote = reason;
            await audit.AppendAsync(ctx.CompanyId, ctx, "sales.adjustment_cancel", "adjustment_request", r.Id.ToString(), new { r.Number, reason });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<AdjustmentRequestRow>> RequestsAsync(SessionContext ctx, Guid? invoiceId = null)
    {
        ctx.Require(Permissions.ReportView);
        var list = await db.AdjustmentRequests.AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == ctx.CompanyId && (invoiceId == null || r.InvoiceId == invoiceId)).ToListAsync();
        var ids = list.Select(r => r.InvoiceId).Distinct().ToList();
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var rows = new List<AdjustmentRequestRow>();
        foreach (var r in list)
        {
            var inv = invs[r.InvoiceId]; var p = partners[inv.PartnerId];
            var views = new List<AdjustmentRequestLineView>();
            foreach (var l in r.Lines.OrderBy(x => x.OriginalLineNo))
            {
                var ol = inv.Lines.First(x => x.LineNo == l.OriginalLineNo); var it = items[ol.ItemId!.Value];
                var used = r.Kind == AdjustmentKind.Return ? await UsedQuantityAsync(inv.Id, l.OriginalLineNo) : await UsedAmountAsync(inv.Id, l.OriginalLineNo);
                views.Add(new AdjustmentRequestLineView(l.OriginalLineNo, it.Code, it.Name, ol.StockQuantity ?? 0, ol.FunctionalAmount, l.Quantity, l.Amount, used, l.Note));
            }
            rows.Add(new AdjustmentRequestRow(r.Id, r.Number, inv.Id, inv.Number, p.Code, p.Name, r.Kind, r.Reason, r.Status, r.DecisionNote, r.CreatedAt, r.Lines.Sum(x => x.Amount ?? 0), r.Lines.Sum(x => x.Quantity ?? 0), views));
        }
        return rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    private Task<decimal> UsedQuantityAsync(Guid invoiceId, int lineNo) => ReturnUsedAsync(db, invoiceId, lineNo);

    /// <summary>
    /// Số lượng đã nhận trả lại của một dòng hóa đơn bán gốc: lớn hơn của (tổng các phiếu nhập hàng trả lại còn hiệu lực gắn với dòng đó) và (tổng trên các hóa đơn trả hàng nháp / đã ghi sổ),
    /// để vừa chặn lập nhiều phiếu nhập khi chưa có hóa đơn, vừa không bỏ sót hóa đơn gắn phiếu nhập lập tay.
    /// </summary>
    internal static async Task<decimal> ReturnUsedAsync(HakDbContext db, Guid invoiceId, int lineNo)
    {
        var receipts = (await (from l in db.StockDocumentLines join d in db.StockDocuments on l.DocumentId equals d.Id
                               where l.SourceInvoiceId == invoiceId && l.SourceInvoiceLineNo == lineNo && (d.Status == StockDocumentStatus.Draft || d.Status == StockDocumentStatus.Posted)
                               select l.Quantity).ToListAsync()).Sum();
        var invoiced = (await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                               where i.AdjustsInvoiceId == invoiceId && i.Purpose == InvoicePurpose.Return && l.AdjustsLineNo == lineNo && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted)
                               select l.StockQuantity).ToListAsync()).Sum(x => x ?? 0);
        return Math.Max(receipts, invoiced);
    }

    private async Task<decimal> UsedAmountAsync(Guid invoiceId, int lineNo)
        => (await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                   where i.AdjustsInvoiceId == invoiceId && i.Purpose == InvoicePurpose.Discount && l.AdjustsLineNo == lineNo && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted)
                   select l.FunctionalAmount).ToListAsync()).Sum();

    // ------------------------------------------------------------------ xóa nợ

    /// <summary>Các khoản phải thu đang mở của khách hàng đến ngày <paramref name="asOf"/> (đã trừ các khoản đã nằm trong phiếu xóa nợ còn hiệu lực).</summary>
    public async Task<IReadOnlyList<WriteOffCandidate>> CandidatesAsync(SessionContext ctx, Guid partnerId, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var ar = await ReceivableAccountAsync(ctx);
        var open = await new SettlementService(db, audit, clock).OpenItemsAsync(ctx, ar.Id, partnerId, asOf);
        var taken = await TakenAsync(ctx.CompanyId, null);
        return open.Where(o => o.Side == Side.Debit).Select(o => new WriteOffCandidate(o.JournalLineId, o.VoucherNumber, o.PostingDate, o.DueDate, o.Open - taken.GetValueOrDefault(o.JournalLineId),
                Math.Max(asOf.DayNumber - o.DueDate.DayNumber, 0), o.Description)).Where(c => c.Open > 0).OrderBy(c => c.PostingDate).ToList();
    }

    private async Task<Dictionary<Guid, decimal>> TakenAsync(Guid companyId, Guid? exclude)
    {
        var rows = await (from i in db.DebtWriteOffItems join w in db.DebtWriteOffs on i.WriteOffId equals w.Id
                          where w.CompanyId == companyId && w.Id != exclude && w.Status != ApprovalStatus.Rejected && w.Status != ApprovalStatus.Cancelled
                                && (w.VoucherId == null || db.Vouchers.Any(v => v.Id == w.VoucherId && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed))
                          select new { i.JournalLineId, i.Amount }).AsNoTracking().ToListAsync();
        return rows.GroupBy(r => r.JournalLineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
    }

    public async Task<DebtWriteOff> RequestWriteOffAsync(SessionContext ctx, WriteOffInput input)
    {
        ctx.Require(Permissions.SettlementManage);
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do xóa nợ.");
        if (string.IsNullOrWhiteSpace(input.Basis)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải ghi căn cứ (số quyết định, biên bản, hồ sơ đòi nợ).");
        if (input.Items.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Chưa chọn khoản nợ nào.");
        if (input.Items.Select(i => i.JournalLineId).Distinct().Count() != input.Items.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi khoản chỉ chọn một lần.");
        if (input.Items.Any(i => i.Amount <= 0 || i.ProvisionUsed < 0 || i.ProvisionUsed > i.Amount)) throw new DomainException(ErrorCodes.ValidationFailed, "Số tiền xóa phải lớn hơn 0; phần dự phòng không âm và không vượt số xóa.");
        return await db.InTransactionAsync(async () =>
        {
            if (!await db.BusinessPartners.AnyAsync(p => p.Id == input.PartnerId && p.CompanyId == ctx.CompanyId)) throw new DomainException(ErrorCodes.PartnerWrongCompany, "Khách hàng không tồn tại.");
            var open = (await CandidatesAsync(ctx, input.PartnerId, DateOnly.FromDateTime(clock.UtcNow))).ToDictionary(c => c.JournalLineId);
            foreach (var i in input.Items)
            {
                if (!open.TryGetValue(i.JournalLineId, out var c)) throw new DomainException(ErrorCodes.NotFound, "Có khoản không còn mở, không thuộc khách hàng này hoặc đã nằm trong phiếu xóa nợ khác.");
                if (i.Amount > c.Open) throw new DomainException(ErrorCodes.SettlementExceedsOpen, $"Khoản {c.VoucherNumber}: xóa {i.Amount} vượt số còn mở {c.Open}.");
            }
            var w = new DebtWriteOff { Id = Ids.New(), CompanyId = ctx.CompanyId, PartnerId = input.PartnerId, Reason = input.Reason.Trim(), Basis = input.Basis.Trim(), CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow };
            w.Number = await NextNumberAsync(ctx.CompanyId, DateOnly.FromDateTime(clock.UtcNow), "XN");
            foreach (var i in input.Items) w.Items.Add(new DebtWriteOffItem { Id = Ids.New(), WriteOffId = w.Id, JournalLineId = i.JournalLineId, Amount = i.Amount, ProvisionUsed = i.ProvisionUsed });
            db.DebtWriteOffs.Add(w);
            await audit.AppendAsync(ctx.CompanyId, ctx, "debt_writeoff.request", "debt_writeoff", w.Id.ToString(), new { w.Number, partner = input.PartnerId, total = input.Items.Sum(i => i.Amount), w.Reason, w.Basis });
            await db.SaveChangesAsync();
            return w;
        });
    }

    public async Task<DebtWriteOff> DecideWriteOffAsync(SessionContext ctx, Guid id, bool approve, string? note)
    {
        ctx.Require(Permissions.CreditOverride);   // người có thẩm quyền xử lý nợ khó đòi
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối phải ghi lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var w = await db.DebtWriteOffs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu xóa nợ không tồn tại.");
            if (w.Status != ApprovalStatus.Requested) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {w.Number} không ở trạng thái chờ duyệt.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && w.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt xóa nợ phải khác người lập phiếu.");
            w.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected; w.DecisionNote = note; w.DecidedBy = ctx.UserId; w.DecidedAt = clock.UtcNow;
            await audit.AppendAsync(ctx.CompanyId, ctx, approve ? "debt_writeoff.approve" : "debt_writeoff.reject", "debt_writeoff", w.Id.ToString(), new { w.Number, note });
            await db.SaveChangesAsync();
            return w;
        });
    }

    public async Task CancelWriteOffAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.SettlementManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var w = await db.DebtWriteOffs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu xóa nợ không tồn tại.");
            if (w.Status is not (ApprovalStatus.Requested or ApprovalStatus.Approved)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {w.Number} đã {(w.Status == ApprovalStatus.Rejected ? "bị từ chối" : "hủy")}.");
            if (await LiveVoucherAsync(w.VoucherId) is { } v) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đã lập chứng từ {v.Number}; hủy hoặc đảo chứng từ trước.");
            w.Status = ApprovalStatus.Cancelled; w.DecisionNote = reason;
            await audit.AppendAsync(ctx.CompanyId, ctx, "debt_writeoff.cancel", "debt_writeoff", w.Id.ToString(), new { w.Number, reason });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Chứng từ nháp xóa nợ: Có TK phải thu theo từng khoản (bù trừ), Nợ dự phòng phần đã trích, Nợ chi phí phần còn lại.</summary>
    public async Task<Voucher> CreateWriteOffVoucherAsync(SessionContext ctx, Guid id, DateOnly date)
    {
        ctx.Require(Permissions.SettlementManage); ctx.Require(Permissions.VoucherCreate);
        return await db.InTransactionAsync(async () =>
        {
            var w = await db.DebtWriteOffs.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu xóa nợ không tồn tại.");
            if (w.Status != ApprovalStatus.Approved) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {w.Number} chưa được duyệt.");
            if (await LiveVoucherAsync(w.VoucherId) is { } live) throw new DomainException(ErrorCodes.DuplicateSource, $"Đã có chứng từ {live.Number}.");
            var ar = await ReceivableAccountAsync(ctx);
            var provision = w.Items.Sum(i => i.ProvisionUsed); var total = w.Items.Sum(i => i.Amount); var expense = total - provision;
            var lines = new List<VoucherLineInput>();
            var lineAccounts = await db.JournalLines.AsNoTracking().Where(l => w.Items.Select(i => i.JournalLineId).Contains(l.Id)).ToDictionaryAsync(l => l.Id);
            foreach (var i in w.Items)
            {
                var jl = lineAccounts[i.JournalLineId];
                lines.Add(new VoucherLineInput(jl.AccountId, Side.Credit, i.Amount, jl.PartnerId, Description: $"Xóa nợ {w.Number}", SettleJournalLineId: i.JournalLineId));
            }
            if (provision > 0) lines.Add(new VoucherLineInput((await RoleAsync(ctx, "BAD_DEBT_PROVISION", "dự phòng phải thu khó đòi")).Id, Side.Debit, provision, Description: $"Dùng dự phòng xóa nợ {w.Number}"));
            if (expense > 0) lines.Add(new VoucherLineInput((await RoleAsync(ctx, "BAD_DEBT_EXPENSE", "chi phí xóa nợ")).Id, Side.Debit, expense, Description: $"Chi phí xóa nợ {w.Number}"));
            _ = ar;
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Xóa nợ phải thu {w.Number}: {w.Reason} (căn cứ: {w.Basis})", lines, SourceSystem: Source, SourceId: w.Id.ToString()));
            w.VoucherId = v.Id;
            await audit.AppendAsync(ctx.CompanyId, ctx, "debt_writeoff.voucher", "debt_writeoff", w.Id.ToString(), new { w.Number, total, provision, expense, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    public async Task<IReadOnlyList<WriteOffRow>> WriteOffsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var list = await db.DebtWriteOffs.AsNoTracking().Include(w => w.Items).Where(w => w.CompanyId == ctx.CompanyId).ToListAsync();
        var vids = list.Where(w => w.VoucherId != null).Select(w => w.VoucherId!.Value).ToList();
        var vs = await db.Vouchers.AsNoTracking().Where(v => vids.Contains(v.Id)).Select(v => new { v.Id, v.Number, v.Status }).ToDictionaryAsync(v => v.Id);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var jids = list.SelectMany(w => w.Items.Select(i => i.JournalLineId)).ToList();
        var jls = await db.JournalLines.AsNoTracking().Where(l => jids.Contains(l.Id)).ToDictionaryAsync(l => l.Id);
        var entries = await db.JournalEntries.AsNoTracking().Where(e => jls.Values.Select(l => l.JournalEntryId).Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        return list.Select(w =>
        {
            var v = w.VoucherId is { } id && vs.TryGetValue(id, out var x) && x.Status is not (VoucherStatus.Cancelled or VoucherStatus.Reversed) ? x : null;
            var p = partners[w.PartnerId];
            return new WriteOffRow(w.Id, w.Number, p.Id, p.Code, p.Name, w.Reason, w.Basis, w.Status, w.DecisionNote, v?.Number, v is { Status: not VoucherStatus.Posted }, v is { Status: VoucherStatus.Posted },
                w.Items.Sum(i => i.Amount), w.Items.Sum(i => i.ProvisionUsed), w.CreatedAt,
                w.Items.Select(i => new WriteOffItemView(i.JournalLineId, entries[jls[i.JournalLineId].JournalEntryId].VoucherNumber, jls[i.JournalLineId].PostingDate, i.Amount, i.ProvisionUsed)).ToList());
        }).OrderByDescending(r => r.CreatedAt).ToList();
    }

    // ------------------------------------------------------------------ hỗ trợ

    private async Task<Voucher?> LiveVoucherAsync(Guid? id)
    {
        if (id is null) return null;
        var v = await db.Vouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return v is null || v.Status is VoucherStatus.Cancelled or VoucherStatus.Reversed ? null : v;
    }

    private Task<Account> ReceivableAccountAsync(SessionContext ctx) => RoleAsync(ctx, "AR_TRADE", "phải thu khách hàng");

    private async Task<Account> RoleAsync(SessionContext ctx, string role, string what)
    {
        var m = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == ctx.CompanyId && x.RoleCode == role)
                ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role} ({what}).");
        return await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == m.AccountId);
    }

    private async Task<string> NextNumberAsync(Guid companyId, DateOnly date, string prefix)
    {
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
                 ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date.Vn()}.");
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == prefix);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = prefix });
        seq.LastNumber++; seq.RowVersion++;
        return $"{prefix}-{fy.Code}-{seq.LastNumber:D5}";
    }
}
