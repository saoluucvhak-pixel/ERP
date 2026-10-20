using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record SupplierRatingRow(Guid PartnerId, string Code, string Name, SupplierStatus Status, string? StatusReason, int Receipts, decimal PurchaseAmount,
    decimal ReceivedQty, decimal AcceptedQty, decimal? AcceptRate, int OrderedReceipts, int OnTimeReceipts, decimal? OnTimeRate, int? Score, string? Grade);

public sealed record ReorderOrderLine(Guid ItemId, decimal Quantity, decimal UnitPrice);

/// <summary>
/// Nhà cung cấp trong chu trình cung ứng (tệp phân tích ERP, mục V.2.1–V.2.2): phê duyệt nhà cung cấp, xếp hạng uy tín và tạo đơn mua nháp từ danh mục cần đặt hàng.
/// Điểm uy tín tính khi xem từ biên bản kiểm nghiệm và phiếu nhập (không lưu): 60% tỷ lệ hàng đạt kiểm nghiệm + 40% tỷ lệ giao đúng hạn;
/// chỉ có một chỉ tiêu thì lấy chỉ tiêu đó; chưa có dữ liệu thì không xếp hạng. Hạng A ≥ 90, B ≥ 75, còn lại C.
/// </summary>
public sealed class SupplierService(HakDbContext db, OrderService orders, AuditService audit, IClock clock)
{
    /// <summary>Bật/tắt chính sách duyệt nhà cung cấp mới của công ty. Chỉ áp dụng cho nhà cung cấp tạo sau đó.</summary>
    public async Task SetApprovalPolicyAsync(SessionContext ctx, bool require)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            var c = await db.Companies.FirstAsync(x => x.Id == ctx.CompanyId);
            if (c.RequireSupplierApproval == require) return 0;
            c.RequireSupplierApproval = require; c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "supplier.approval_policy", "company", c.Id.ToString(), new { require }, c.RowVersion);
            await db.SaveChangesAsync();
            return 0;
        });
    }

    public async Task<bool> ApprovalPolicyAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.VoucherView);
        return await db.Companies.AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.RequireSupplierApproval).FirstAsync();
    }

    /// <summary>
    /// Duyệt / chặn / đưa về chờ duyệt. Duyệt cần quyền voucher.approve và (khi công ty bắt buộc tách người) người duyệt phải khác người tạo nhà cung cấp;
    /// chặn và đưa về chờ duyệt cần quyền quản lý danh mục và bắt buộc lý do.
    /// </summary>
    public async Task<BusinessPartner> SetStatusAsync(SessionContext ctx, Guid partnerId, SupplierStatus status, string? reason)
    {
        ctx.Require(status == SupplierStatus.Approved ? Permissions.VoucherApprove : Permissions.CatalogManage);
        if (status != SupplierStatus.Approved && string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ReasonRequired, "Chặn hoặc đưa về chờ duyệt nhà cung cấp phải có lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var p = await db.BusinessPartners.FirstOrDefaultAsync(x => x.Id == partnerId && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Đối tượng không tồn tại.");
            if (!p.Roles.HasFlag(PartnerRoles.Supplier)) throw new DomainException(ErrorCodes.ValidationFailed, "Đối tượng không phải nhà cung cấp.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (status == SupplierStatus.Approved && company.RequireDistinctApprover && p.CreatedBy == ctx.UserId)
                throw new DomainException(ErrorCodes.SodViolation, "Người duyệt nhà cung cấp không được trùng người tạo.");
            var old = p.SupplierStatus;
            p.SupplierStatus = status;
            p.SupplierStatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            p.SupplierDecidedBy = ctx.UserId; p.SupplierDecidedAt = clock.UtcNow;
            p.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "supplier.set_status", "business_partner", p.Id.ToString(), new { p.Code, oldStatus = old.ToString(), newStatus = status.ToString(), p.SupplierStatusReason }, p.RowVersion);
            await db.SaveChangesAsync();
            return p;
        });
    }

    /// <summary>Xếp hạng nhà cung cấp trong kỳ theo ngày phiếu nhập đã ghi sổ.</summary>
    public async Task<IReadOnlyList<SupplierRatingRow>> RatingAsync(SessionContext ctx, DateOnly fromDate, DateOnly toDate)
    {
        ctx.Require(Permissions.ReportView);
        var suppliers = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId && (p.Roles & PartnerRoles.Supplier) != 0).ToListAsync();
        var docs = await db.StockDocuments.AsNoTracking().Include(d => d.Lines)
            .Where(d => d.CompanyId == ctx.CompanyId && d.Type == StockDocumentType.Receipt && d.Status == StockDocumentStatus.Posted && d.PartnerId != null
                        && d.DocumentDate >= fromDate && d.DocumentDate <= toDate).ToListAsync();
        var orderIds = docs.SelectMany(d => d.Lines).Where(l => l.OrderId != null).Select(l => l.OrderId!.Value).Distinct().ToList();
        var expected = await db.TradeOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.ExpectedDate);
        var docIds = docs.Select(d => d.Id).ToList();
        var insp = await db.GoodsInspections.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && !i.IsCancelled && docIds.Contains(i.StockDocumentId)).ToListAsync();
        var inspByDoc = insp.GroupBy(i => i.StockDocumentId).ToDictionary(g => g.Key, g => g.SelectMany(i => i.Lines).ToList());

        var rows = new List<SupplierRatingRow>();
        foreach (var p in suppliers.OrderBy(s => s.Code, StringComparer.Ordinal))
        {
            var mine = docs.Where(d => d.PartnerId == p.Id).ToList();
            decimal received = 0, accepted = 0, amount = mine.Sum(d => d.Lines.Sum(l => l.Amount ?? 0));
            int ordered = 0, onTime = 0;
            foreach (var d in mine)
            {
                if (inspByDoc.TryGetValue(d.Id, out var ls)) { received += ls.Sum(l => l.ReceivedQty); accepted += ls.Sum(l => l.AcceptedQty); }
                var dues = d.Lines.Where(l => l.OrderId != null).Select(l => expected.GetValueOrDefault(l.OrderId!.Value)).Where(e => e != null).Select(e => e!.Value).ToList();
                if (dues.Count == 0) continue;
                ordered++;
                if (d.DocumentDate <= dues.Min()) onTime++;
            }
            decimal? acceptRate = received > 0 ? Math.Round(accepted / received * 100, 1) : null;
            decimal? onTimeRate = ordered > 0 ? Math.Round((decimal)onTime / ordered * 100, 1) : null;
            int? score = (acceptRate, onTimeRate) switch
            {
                (null, null) => null,
                ({ } a, null) => (int)Math.Round(a, MidpointRounding.AwayFromZero),
                (null, { } t) => (int)Math.Round(t, MidpointRounding.AwayFromZero),
                ({ } a, { } t) => (int)Math.Round(a * 0.6m + t * 0.4m, MidpointRounding.AwayFromZero),
            };
            var grade = score is null ? null : score >= 90 ? "A" : score >= 75 ? "B" : "C";
            rows.Add(new SupplierRatingRow(p.Id, p.Code, p.Name, p.SupplierStatus, p.SupplierStatusReason, mine.Count, amount, received, accepted, acceptRate, ordered, onTime, onTimeRate, score, grade));
        }
        return rows;
    }

    /// <summary>Tạo đơn mua Nháp từ các dòng của danh mục cần đặt hàng (khâu nhận biết nhu cầu → đặt hàng). Đơn vẫn phải được người khác xác nhận.</summary>
    public async Task<TradeOrder> CreateOrderFromReorderAsync(SessionContext ctx, Guid partnerId, IReadOnlyList<ReorderOrderLine> lines, DateOnly? expectedDate = null)
    {
        if (lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Chưa chọn vật tư nào để đặt.");
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return await orders.CreateAsync(ctx, new OrderInput(InvoiceKind.Purchase, partnerId, today, "Đặt hàng bổ sung theo danh mục cần đặt hàng",
            lines.Select(l => new OrderLineInput(l.ItemId, l.Quantity, l.UnitPrice)).ToList(), expectedDate));
    }
}
