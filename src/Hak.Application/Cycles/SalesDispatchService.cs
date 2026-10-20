using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record DispatchLineInput(int OrderLineNo, decimal Quantity);
public sealed record DispatchInput(Guid OrderId, Guid WarehouseId, DateOnly DispatchDate, string? ShipTo, string? Note, IReadOnlyList<DispatchLineInput> Lines);
public sealed record DeliveryLineInput(int DispatchLineNo, decimal Quantity);
public sealed record DeliveryInput(Guid DispatchOrderId, DateOnly DeliveryDate, string? ShipTo, string? Carrier, string? DeliveryStaff, string? WaybillNo, string? Note,
    IReadOnlyList<DeliveryLineInput> Lines);

public sealed record DispatchLineView(int LineNo, int OrderLineNo, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal Quantity, decimal Delivered, decimal Outstanding);
public sealed record DeliveryLineView(int LineNo, int DispatchLineNo, string ItemCode, string ItemName, string Unit, decimal Quantity);
public sealed record DeliveryView(Guid Id, string Number, DateOnly DeliveryDate, string? ShipTo, string? Carrier, string? DeliveryStaff, string? WaybillNo, string? Note,
    Guid StockDocumentId, string StockNumber, string Stage, string? InvoiceNumber, Guid? InvoiceId, IReadOnlyList<DeliveryLineView> Lines);
public sealed record DispatchView(Guid Id, string Number, Guid OrderId, string OrderNumber, Guid PartnerId, string PartnerName, string? PartnerAddress, Guid WarehouseId, string WarehouseCode,
    DateOnly DispatchDate, string? ShipTo, string? Note, string Status, string? CancelReason, IReadOnlyList<DispatchLineView> Lines, IReadOnlyList<DeliveryView> Deliveries);

/// <summary>
/// Chu trình doanh thu theo tệp phân tích ERP (docs/10, mục 2): lệnh bán hàng, phiếu giao hàng (sinh phiếu xuất kho nháp) và lập hóa đơn bán
/// từ phiếu giao hàng đã xuất kho (tự điền giá, thuế suất và hạn thanh toán). Lệnh bán hàng và phiếu giao hàng không ghi Nhật ký;
/// bút toán chỉ phát sinh khi ghi sổ phiếu xuất kho và hóa đơn (do người khác ghi sổ, như các chứng từ kho/hóa đơn khác).
/// </summary>
public sealed class SalesDispatchService(HakDbContext db, StockService stock, InvoiceService invoices, AuditService audit, IClock clock)
{
    public static class Stages
    {
        public const string AwaitingStockPosting = "AWAITING_STOCK_POSTING";   // phiếu xuất kho còn Nháp
        public const string Delivered = "DELIVERED";                            // phiếu xuất kho đã ghi sổ, chưa có hóa đơn
        public const string InvoiceDraft = "INVOICE_DRAFT";
        public const string Invoiced = "INVOICED";
        public const string Reversed = "REVERSED";
        public const string Cancelled = "CANCELLED";
    }

    // ---------------------------------------------------------------- lệnh bán hàng

    public async Task<SalesDispatchOrder> CreateDispatchAsync(SessionContext ctx, DispatchInput input)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (input.Lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Lệnh bán hàng chưa có dòng.");
        return await db.InTransactionAsync(async () =>
        {
            var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == input.OrderId && o.CompanyId == ctx.CompanyId)
                        ?? throw new DomainException(ErrorCodes.NotFound, "Đơn đặt hàng không tồn tại.");
            if (order.Kind != InvoiceKind.Sales) throw new DomainException(ErrorCodes.ValidationFailed, "Lệnh bán hàng chỉ lập cho đơn bán.");
            if (order.Status != OrderStatus.Confirmed) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đơn {order.Number} phải đã xác nhận mới lập lệnh bán hàng.");
            if (!await db.Warehouses.AnyAsync(w => w.Id == input.WarehouseId && w.CompanyId == ctx.CompanyId))
                throw new DomainException(ErrorCodes.NotFound, "Kho xuất hàng không thuộc công ty.");
            var fy = await FiscalYearAsync(ctx.CompanyId, input.DispatchDate);

            var already = await (from l in db.SalesDispatchLines join o in db.SalesDispatchOrders on l.DispatchOrderId equals o.Id
                                 where o.OrderId == order.Id && o.Status != DispatchOrderStatus.Cancelled select new { l.OrderLineNo, l.Quantity }).ToListAsync();
            var dup = input.Lines.GroupBy(l => l.OrderLineNo).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng đơn {dup.Key} xuất hiện hai lần trong lệnh.");
            foreach (var l in input.Lines)
            {
                var ol = order.Lines.FirstOrDefault(x => x.LineNo == l.OrderLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Đơn {order.Number} không có dòng {l.OrderLineNo}.");
                if (l.Quantity <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Số lượng lệnh bán hàng phải lớn hơn 0.");
                var limit = ol.Quantity * (1 + order.OverDeliveryTolerancePercent / 100m);
                var total = already.Where(a => a.OrderLineNo == l.OrderLineNo).Sum(a => a.Quantity) + l.Quantity;
                if (total > limit)
                    throw new DomainException(ErrorCodes.OverDelivered, $"Dòng {l.OrderLineNo} của đơn {order.Number}: các lệnh bán hàng đạt {total} so với đặt {ol.Quantity} (dung sai {order.OverDeliveryTolerancePercent}%).");
            }

            var d = new SalesDispatchOrder
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Number = await NextNumberAsync(ctx.CompanyId, fy, "LBH", "LBH"), OrderId = order.Id, PartnerId = order.PartnerId,
                WarehouseId = input.WarehouseId, DispatchDate = input.DispatchDate, ShipTo = Clean(input.ShipTo), Note = Clean(input.Note), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
            };
            db.SalesDispatchOrders.Add(d);
            var n = 0;
            foreach (var l in input.Lines.OrderBy(x => x.OrderLineNo))
                db.SalesDispatchLines.Add(new SalesDispatchLine { Id = Ids.New(), DispatchOrderId = d.Id, CompanyId = ctx.CompanyId, LineNo = ++n, OrderLineNo = l.OrderLineNo,
                    ItemId = order.Lines.First(x => x.LineNo == l.OrderLineNo).ItemId, Quantity = l.Quantity });
            await audit.AppendAsync(ctx.CompanyId, ctx, "sales_dispatch.create", "sales_dispatch_order", d.Id.ToString(), new { d.Number, order = order.Number, lines = n }, d.RowVersion);
            await db.SaveChangesAsync();
            return d;
        });
    }

    /// <summary>Phát hành lệnh bán hàng: kho được giao hàng theo lệnh (các liên chuyển cho kho, chuẩn bị hàng, lập hóa đơn, khách hàng).</summary>
    public Task<SalesDispatchOrder> IssueAsync(SessionContext ctx, Guid id) => DispatchTransitionAsync(ctx, id, Permissions.InvoiceDocument, "issue", d =>
    {
        if (d.Status != DispatchOrderStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {d.Number} đang ở trạng thái {d.Status}.");
        d.Status = DispatchOrderStatus.Issued; d.IssuedBy = ctx.UserId; d.IssuedAt = clock.UtcNow;
        return Task.CompletedTask;
    });

    public Task<SalesDispatchOrder> CancelDispatchAsync(SessionContext ctx, Guid id, string reason) => DispatchTransitionAsync(ctx, id, Permissions.InvoiceDocument, "cancel", async d =>
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy lệnh.");
        if (d.Status == DispatchOrderStatus.Cancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, "Lệnh đã hủy.");
        if (await db.SalesDeliveries.AnyAsync(x => x.DispatchOrderId == d.Id && !x.IsCancelled))
            throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {d.Number} đã có phiếu giao hàng; hủy phiếu giao hàng trước.");
        d.Status = DispatchOrderStatus.Cancelled; d.CancelReason = reason.Trim();
    });

    // ---------------------------------------------------------------- phiếu giao hàng

    /// <summary>Lập phiếu giao hàng từ lệnh đã phát hành; sinh phiếu xuất kho Nháp (TK đối ứng là vai trò COGS) liên kết dòng đơn hàng.</summary>
    public async Task<SalesDelivery> CreateDeliveryAsync(SessionContext ctx, DeliveryInput input)
    {
        ctx.Require(Permissions.StockDocument);
        if (input.Lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Phiếu giao hàng chưa có dòng.");
        return await db.InTransactionAsync(async () =>
        {
            var d = await db.SalesDispatchOrders.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == input.DispatchOrderId && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Lệnh bán hàng không tồn tại.");
            if (d.Status != DispatchOrderStatus.Issued) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {d.Number} phải đã phát hành mới giao hàng.");
            var order = await db.TradeOrders.AsNoTracking().FirstAsync(o => o.Id == d.OrderId);
            var cogs = (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "COGS"))?.AccountId
                       ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, "Chưa ánh xạ vai trò tài khoản COGS (tài khoản đối ứng của phiếu xuất kho).");
            var fy = await FiscalYearAsync(ctx.CompanyId, input.DeliveryDate);
            var delivered = await (from l in db.SalesDeliveryLines join x in db.SalesDeliveries on l.DeliveryId equals x.Id
                                   where x.DispatchOrderId == d.Id && !x.IsCancelled select new { l.DispatchLineNo, l.Quantity }).ToListAsync();
            var dup = input.Lines.GroupBy(l => l.DispatchLineNo).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng lệnh {dup.Key} xuất hiện hai lần trong phiếu.");
            foreach (var l in input.Lines)
            {
                var dl = d.Lines.FirstOrDefault(x => x.LineNo == l.DispatchLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Lệnh {d.Number} không có dòng {l.DispatchLineNo}.");
                if (l.Quantity <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Số lượng giao phải lớn hơn 0.");
                var limit = dl.Quantity * (1 + order.OverDeliveryTolerancePercent / 100m);
                var total = delivered.Where(x => x.DispatchLineNo == l.DispatchLineNo).Sum(x => x.Quantity) + l.Quantity;
                if (total > limit) throw new DomainException(ErrorCodes.OverDelivered, $"Dòng {l.DispatchLineNo} của lệnh {d.Number}: giao {total} so với lệnh {dl.Quantity} (dung sai {order.OverDeliveryTolerancePercent}%).");
            }

            var lines = input.Lines.OrderBy(l => l.DispatchLineNo).ToList();
            var doc = await stock.CreateDocumentAsync(ctx, new StockDocumentInput(StockDocumentType.Issue, d.WarehouseId, input.DeliveryDate, input.DeliveryDate,
                $"Giao hàng theo lệnh {d.Number}", lines.Select(l =>
                {
                    var dl = d.Lines.First(x => x.LineNo == l.DispatchLineNo);
                    return new StockLineInput(dl.ItemId, l.Quantity, CounterAccountId: cogs, OrderId: d.OrderId, OrderLineNo: dl.OrderLineNo);
                }).ToList(), PartnerId: d.PartnerId));

            var del = new SalesDelivery
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Number = await NextNumberAsync(ctx.CompanyId, fy, "PGH", "PGH"), DispatchOrderId = d.Id, StockDocumentId = doc.Id,
                DeliveryDate = input.DeliveryDate, ShipTo = Clean(input.ShipTo) ?? d.ShipTo, Carrier = Clean(input.Carrier), DeliveryStaff = Clean(input.DeliveryStaff),
                WaybillNo = Clean(input.WaybillNo), Note = Clean(input.Note), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
            };
            db.SalesDeliveries.Add(del);
            var n = 0;
            foreach (var l in lines)
            {
                n++;
                db.SalesDeliveryLines.Add(new SalesDeliveryLine { Id = Ids.New(), DeliveryId = del.Id, CompanyId = ctx.CompanyId, LineNo = n, DispatchLineNo = l.DispatchLineNo,
                    ItemId = d.Lines.First(x => x.LineNo == l.DispatchLineNo).ItemId, Quantity = l.Quantity, StockLineNo = n });
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "sales_delivery.create", "sales_delivery", del.Id.ToString(), new { del.Number, dispatch = d.Number, stock = doc.Number, del.WaybillNo }, del.RowVersion);
            await db.SaveChangesAsync();
            return del;
        });
    }

    /// <summary>Hủy phiếu giao hàng: chỉ khi phiếu xuất kho còn Nháp và chưa có hóa đơn.</summary>
    public async Task<SalesDelivery> CancelDeliveryAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.StockDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy phiếu giao hàng.");
        return await db.InTransactionAsync(async () =>
        {
            var del = await db.SalesDeliveries.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu giao hàng không tồn tại.");
            if (del.IsCancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, "Phiếu giao hàng đã hủy.");
            var doc = await db.StockDocuments.AsNoTracking().FirstAsync(x => x.Id == del.StockDocumentId);
            if (doc.Status != StockDocumentStatus.Draft)
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu xuất kho {doc.Number} đã ghi sổ; đảo phiếu xuất kho ở màn Kho trước khi hủy phiếu giao hàng.");
            await stock.CancelDocumentAsync(ctx, doc.Id, reason);
            del.IsCancelled = true; del.CancelReason = reason.Trim(); del.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "sales_delivery.cancel", "sales_delivery", del.Id.ToString(), new { del.Number, reason }, del.RowVersion);
            await db.SaveChangesAsync();
            return del;
        });
    }

    // ---------------------------------------------------------------- hóa đơn bán từ phiếu giao hàng

    /// <summary>
    /// Lập hóa đơn bán Nháp từ phiếu giao hàng đã xuất kho: giá, cơ sở tính giá (số lượng hoặc BDMT) và thuế suất lấy từ dòng đơn đặt hàng
    /// (nếu đơn để giá 0 thì lấy giá bán và thuế suất mặc định của danh mục hàng hóa); hạn thanh toán = ngày hóa đơn + điều kiện thanh toán của khách.
    /// Ký hiệu và số hóa đơn do người lập nhập (số hóa đơn theo quy định, không tự sinh).
    /// </summary>
    public async Task<CommercialInvoice> CreateInvoiceAsync(SessionContext ctx, Guid deliveryId, string invoiceSeries, string invoiceNo, DateOnly invoiceDate)
    {
        ctx.Require(Permissions.InvoiceDocument);
        var del = await db.SalesDeliveries.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == deliveryId && x.CompanyId == ctx.CompanyId)
                  ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu giao hàng không tồn tại.");
        if (del.IsCancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, "Phiếu giao hàng đã hủy.");
        var doc = await db.StockDocuments.AsNoTracking().FirstAsync(x => x.Id == del.StockDocumentId);
        if (doc.Status != StockDocumentStatus.Posted)
            throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu xuất kho {doc.Number} chưa ghi sổ: chưa xuất kho thì chưa lập hóa đơn.");
        var d = await db.SalesDispatchOrders.AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == del.DispatchOrderId);
        var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.Id == d.OrderId);
        if (order.Currency is not null) throw new DomainException(ErrorCodes.ValidationFailed, "Đơn ngoại tệ lập hóa đơn ở màn Mua / Bán (cần tỷ giá).");
        var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == d.PartnerId);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);

        var lines = del.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var dl = d.Lines.First(x => x.LineNo == l.DispatchLineNo);
            var ol = order.Lines.First(x => x.LineNo == dl.OrderLineNo);
            var item = items[l.ItemId];
            var price = ol.UnitPrice > 0 ? ol.UnitPrice : item.SalePrice ?? 0;
            if (price <= 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Chưa có giá bán cho {item.Code}: nhập giá ở đơn hàng hoặc danh mục hàng hóa.");
            return new InvoiceLineInput(price, doc.Id, l.StockLineNo, l.Quantity, ol.Basis, VatRate: ol.VatRate ?? item.DefaultVatRate, OrderId: order.Id, OrderLineNo: ol.LineNo);
        }).ToList();
        DateOnly? due = partner.PaymentTermsDays is { } days ? invoiceDate.AddDays(days) : null;
        return await invoices.CreateAsync(ctx, new InvoiceInput(InvoiceKind.Sales, d.PartnerId, invoiceSeries, invoiceNo, invoiceDate, invoiceDate,
            $"Bán hàng theo phiếu giao hàng {del.Number}", lines, DueDate: due));
    }

    // ---------------------------------------------------------------- queries

    public async Task<IReadOnlyList<DispatchView>> ListAsync(SessionContext ctx, Guid? orderId = null, bool includeCancelled = false)
    {
        ctx.Require(Permissions.VoucherView);
        var q = db.SalesDispatchOrders.AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == ctx.CompanyId);
        if (orderId is { } o) q = q.Where(x => x.OrderId == o);
        if (!includeCancelled) q = q.Where(x => x.Status != DispatchOrderStatus.Cancelled);
        var list = await q.OrderByDescending(x => x.Number).Take(300).ToListAsync();
        var ids = list.Select(x => x.Id).ToList();
        var dels = await db.SalesDeliveries.AsNoTracking().Include(x => x.Lines).Where(x => ids.Contains(x.DispatchOrderId)).OrderBy(x => x.Number).ToListAsync();
        var docIds = dels.Select(x => x.StockDocumentId).ToList();
        var docs = await db.StockDocuments.AsNoTracking().Where(x => docIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var invLines = await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                              where l.StockDocumentId != null && docIds.Contains(l.StockDocumentId.Value) && i.Status != InvoiceStatus.Cancelled && i.Purpose == InvoicePurpose.Normal
                              select new { DocId = l.StockDocumentId!.Value, i.Id, i.Number, i.Status }).AsNoTracking().ToListAsync();
        var orders = await db.TradeOrders.AsNoTracking().Where(x => list.Select(d => d.OrderId).Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var whs = await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == ctx.CompanyId).ToDictionaryAsync(w => w.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);

        return list.Select(d =>
        {
            var mine = dels.Where(x => x.DispatchOrderId == d.Id).ToList();
            var active = mine.Where(x => !x.IsCancelled).SelectMany(x => x.Lines).GroupBy(l => l.DispatchLineNo).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            var lineViews = d.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var done = active.GetValueOrDefault(l.LineNo);
                var it = items[l.ItemId];
                return new DispatchLineView(l.LineNo, l.OrderLineNo, l.ItemId, it.Code, it.Name, it.Unit, l.Quantity, done, Math.Max(0, l.Quantity - done));
            }).ToList();
            var delViews = mine.Select(x =>
            {
                var doc = docs[x.StockDocumentId];
                var inv = invLines.Where(i => i.DocId == x.StockDocumentId).OrderByDescending(i => i.Status == InvoiceStatus.Posted).FirstOrDefault();
                var stage = x.IsCancelled ? Stages.Cancelled : doc.Status switch
                {
                    StockDocumentStatus.Draft => Stages.AwaitingStockPosting,
                    StockDocumentStatus.Reversed => Stages.Reversed,
                    StockDocumentStatus.Cancelled => Stages.Cancelled,
                    _ => inv is null ? Stages.Delivered : inv.Status == InvoiceStatus.Posted ? Stages.Invoiced : Stages.InvoiceDraft,
                };
                return new DeliveryView(x.Id, x.Number, x.DeliveryDate, x.ShipTo, x.Carrier, x.DeliveryStaff, x.WaybillNo, x.Note, x.StockDocumentId, doc.Number, stage, inv?.Number, inv?.Id,
                    x.Lines.OrderBy(l => l.LineNo).Select(l => new DeliveryLineView(l.LineNo, l.DispatchLineNo, items[l.ItemId].Code, items[l.ItemId].Name, items[l.ItemId].Unit, l.Quantity)).ToList());
            }).ToList();
            var p = partners[d.PartnerId];
            return new DispatchView(d.Id, d.Number, d.OrderId, orders[d.OrderId].Number, d.PartnerId, p.Name, p.Address, d.WarehouseId, whs[d.WarehouseId].Code, d.DispatchDate,
                d.ShipTo, d.Note, d.Status.ToString(), d.CancelReason, lineViews, delViews);
        }).ToList();
    }

    // ---------------------------------------------------------------- internals

    private async Task<SalesDispatchOrder> DispatchTransitionAsync(SessionContext ctx, Guid id, string permission, string action, Func<SalesDispatchOrder, Task> apply)
    {
        ctx.Require(permission);
        return await db.InTransactionAsync(async () =>
        {
            var d = await db.SalesDispatchOrders.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Lệnh bán hàng không tồn tại.");
            var from = d.Status;
            await apply(d);
            d.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, $"sales_dispatch.{action}", "sales_dispatch_order", d.Id.ToString(), new { d.Number, from, to = d.Status, d.CancelReason }, d.RowVersion);
            await db.SaveChangesAsync();
            return d;
        });
    }

    private async Task<FiscalYear> FiscalYearAsync(Guid companyId, DateOnly date)
        => await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
           ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date.Vn()}.");

    private async Task<string> NextNumberAsync(Guid companyId, FiscalYear fy, string key, string prefix)
    {
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++;
        seq.RowVersion++;
        return $"{prefix}{fy.Code}-{seq.LastNumber:D6}";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
