using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record InspectionLineInput(int StockLineNo, decimal AcceptedQty, string? Note = null);
public sealed record InspectionInput(Guid StockDocumentId, DateOnly InspectionDate, string Inspector, string? Conclusion, IReadOnlyList<InspectionLineInput>? Lines = null);

public sealed record InspectionLineView(int StockLineNo, string ItemCode, string ItemName, string Unit, decimal ReceivedQty, decimal AcceptedQty, decimal RejectedQty, string? Note);
public sealed record InspectionView(Guid Id, string Number, DateOnly InspectionDate, string Inspector, string? Conclusion, IReadOnlyList<InspectionLineView> Lines);
public sealed record ReceiptInspectionRow(Guid StockDocumentId, string StockNumber, DateOnly DocumentDate, string Status, Guid? PartnerId, string PartnerName, string Description,
    decimal TotalQty, InspectionView? Inspection);

/// <summary>Biên bản kiểm nghiệm cho phiếu nhập kho (chu trình cung ứng). Không ghi Nhật ký; mỗi phiếu nhập có tối đa một biên bản còn hiệu lực.</summary>
public sealed class GoodsInspectionService(HakDbContext db, AuditService audit, IClock clock)
{
    public async Task<GoodsInspection> CreateAsync(SessionContext ctx, InspectionInput input)
    {
        ctx.Require(Permissions.StockDocument);
        if (string.IsNullOrWhiteSpace(input.Inspector)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải ghi người hoặc ban kiểm nghiệm.");
        return await db.InTransactionAsync(async () =>
        {
            var doc = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == input.StockDocumentId && d.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu nhập kho không tồn tại.");
            if (doc.Type != StockDocumentType.Receipt) throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ kiểm nghiệm phiếu nhập kho.");
            if (doc.Status is StockDocumentStatus.Cancelled or StockDocumentStatus.Reversed)
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {doc.Number} đã {(doc.Status == StockDocumentStatus.Cancelled ? "hủy" : "đảo")}.");
            if (await db.GoodsInspections.AnyAsync(i => i.StockDocumentId == doc.Id && !i.IsCancelled))
                throw new DomainException(ErrorCodes.DuplicateSource, $"Phiếu {doc.Number} đã có biên bản kiểm nghiệm; hủy biên bản cũ để lập lại.");
            var given = (input.Lines ?? []).ToDictionary(l => l.StockLineNo);
            foreach (var g in given.Values)
            {
                var line = doc.Lines.FirstOrDefault(l => l.LineNo == g.StockLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Phiếu {doc.Number} không có dòng {g.StockLineNo}.");
                if (g.AcceptedQty < 0 || g.AcceptedQty > line.Quantity)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {g.StockLineNo}: số lượng đạt phải từ 0 đến số lượng nhận {line.Quantity}.");
            }
            var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == ctx.CompanyId && f.StartDate <= input.InspectionDate && f.EndDate >= input.InspectionDate)
                     ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {input.InspectionDate}.");
            var bb = new GoodsInspection
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Number = await NextNumberAsync(ctx.CompanyId, fy), StockDocumentId = doc.Id, PartnerId = doc.PartnerId,
                InspectionDate = input.InspectionDate, Inspector = input.Inspector.Trim(), Conclusion = string.IsNullOrWhiteSpace(input.Conclusion) ? null : input.Conclusion.Trim(),
                CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
            };
            db.GoodsInspections.Add(bb);
            foreach (var l in doc.Lines.OrderBy(x => x.LineNo))   // lines not given are accepted in full
            {
                given.TryGetValue(l.LineNo, out var g);
                db.GoodsInspectionLines.Add(new GoodsInspectionLine { Id = Ids.New(), InspectionId = bb.Id, CompanyId = ctx.CompanyId, StockLineNo = l.LineNo, ItemId = l.ItemId,
                    ReceivedQty = l.Quantity, AcceptedQty = g?.AcceptedQty ?? l.Quantity, Note = string.IsNullOrWhiteSpace(g?.Note) ? null : g!.Note!.Trim() });
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "goods_inspection.create", "goods_inspection", bb.Id.ToString(), new { bb.Number, stock = doc.Number, bb.Inspector }, bb.RowVersion);
            await db.SaveChangesAsync();
            return bb;
        });
    }

    public async Task<GoodsInspection> CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.StockDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy biên bản.");
        return await db.InTransactionAsync(async () =>
        {
            var bb = await db.GoodsInspections.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Biên bản không tồn tại.");
            if (bb.IsCancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, "Biên bản đã hủy.");
            bb.IsCancelled = true; bb.CancelReason = reason.Trim(); bb.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "goods_inspection.cancel", "goods_inspection", bb.Id.ToString(), new { bb.Number, reason }, bb.RowVersion);
            await db.SaveChangesAsync();
            return bb;
        });
    }

    /// <summary>Phiếu nhập kho (không hủy, không đảo) kèm biên bản kiểm nghiệm còn hiệu lực, mới nhất trước.</summary>
    public async Task<IReadOnlyList<ReceiptInspectionRow>> ReceiptsAsync(SessionContext ctx, bool onlyPending = false)
    {
        ctx.Require(Permissions.VoucherView);
        var docs = await db.StockDocuments.AsNoTracking().Include(d => d.Lines)
            .Where(d => d.CompanyId == ctx.CompanyId && d.Type == StockDocumentType.Receipt && d.Status != StockDocumentStatus.Cancelled && d.Status != StockDocumentStatus.Reversed)
            .OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Number).Take(300).ToListAsync();
        var ids = docs.Select(d => d.Id).ToList();
        var bbs = await db.GoodsInspections.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.StockDocumentId) && !i.IsCancelled).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var rows = docs.Select(d =>
        {
            var bb = bbs.FirstOrDefault(i => i.StockDocumentId == d.Id);
            var view = bb is null ? null : new InspectionView(bb.Id, bb.Number, bb.InspectionDate, bb.Inspector, bb.Conclusion, bb.Lines.OrderBy(l => l.StockLineNo)
                .Select(l => new InspectionLineView(l.StockLineNo, items[l.ItemId].Code, items[l.ItemId].Name, items[l.ItemId].Unit, l.ReceivedQty, l.AcceptedQty, l.ReceivedQty - l.AcceptedQty, l.Note)).ToList());
            return new ReceiptInspectionRow(d.Id, d.Number, d.DocumentDate, d.Status.ToString(), d.PartnerId, d.PartnerId is { } p ? partners[p].Name : "", d.Description, d.Lines.Sum(l => l.Quantity), view);
        });
        return (onlyPending ? rows.Where(r => r.Inspection is null) : rows).ToList();
    }

    private async Task<string> NextNumberAsync(Guid companyId, FiscalYear fy)
    {
        const string key = "BBKN";
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++;
        seq.RowVersion++;
        return $"{key}{fy.Code}-{seq.LastNumber:D6}";
    }
}
