using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Production;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Production;

public sealed record BomLineInput(Guid RawItemId, decimal QuantityPerUnit, decimal ScrapRate = 0);

public sealed record BomView(Guid Id, Guid ItemId, string ItemCode, string ItemName, string? Note, IReadOnlyList<BomLineView> Lines);
public sealed record BomLineView(Guid RawItemId, string RawCode, string RawName, string Unit, decimal QuantityPerUnit, decimal ScrapRate);

public sealed record ProductionOrderInput(Guid ItemId, Guid CostObjectId, Guid WarehouseId, decimal PlannedQuantity, DateOnly PlannedStart, string? Note = null);

public sealed record RequirementRow(Guid RawItemId, string Code, string Name, string Unit, decimal Required, decimal Issued, decimal Remaining, decimal OnHand, decimal Shortage);

public sealed record MaterialUsageRow(Guid RawItemId, string Code, string Name, string Unit, decimal StandardQuantity, decimal IssuedQuantity, decimal Variance, decimal Cost);

/// <summary>Bảng tính giá thành của lệnh: chi phí nguyên vật liệu (621), nhân công (622), sản xuất chung (627), tổng dồn vào TK dở dang (154), giá thành đơn vị.</summary>
public sealed record CostSheet(Guid OrderId, string Number, ProductionOrderStatus Status, decimal Quantity, bool QuantityIsActual, decimal MaterialCost, decimal LaborCost, decimal OverheadCost,
    decimal TotalCost, decimal UnitCost, IReadOnlyList<MaterialUsageRow> Materials, decimal LaborHours, decimal MachineHours);

public sealed record ProductionOrderRow(Guid Id, string Number, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal PlannedQuantity, decimal? ActualQuantity,
    DateOnly PlannedStart, DateOnly? CompletedDate, ProductionOrderStatus Status, decimal TotalCost, decimal UnitCost, Guid? ReceiptDocumentId, string? Note);

public sealed record ConfirmationRow(Guid Id, DateOnly WorkDate, string WorkCenterCode, string WorkCenterName, decimal LaborHours, decimal MachineHours, decimal LaborCost, decimal OverheadCost,
    string? Note, bool Voided, string? VoidReason);

public sealed record OrderIssueRow(Guid StockDocumentId, string Number, StockDocumentStatus Status, DateOnly Date);

/// <summary>
/// Chu trình sản xuất theo tài liệu thiết kế (định mức → lệnh sản xuất → xuất nguyên vật liệu → báo công → nhập thành phẩm → giá thành).
/// Nguyên vật liệu xuất bằng phiếu kho thường (đối ứng TK dở dang của đối tượng tính giá thành, giá vốn do kho tính); báo công chỉ ghi giờ;
/// khi hoàn thành lệnh: ghi Nhật ký phân bổ nhân công, sản xuất chung (Nợ dở dang / Có vai trò PROD_LABOR_APPLIED, PROD_OVERHEAD_APPLIED) và lập phiếu nhập
/// thành phẩm Nháp theo giá thành thực tế của lệnh (Có dở dang). Không dùng số hiệu TK pháp lý cố định: TK do kế toán ánh xạ, chờ xác minh.
/// </summary>
public sealed class ProductionOrderService(HakDbContext db, AccountingEngine engine, StockService stock, AuditService audit, IClock clock)
{
    public const string AllocationSource = "HAK.PRODUCTION_ORDER";

    // ------------------------------------------------------------ danh mục: định mức, tổ xưởng

    public async Task<BomView> SaveBomAsync(SessionContext ctx, Guid itemId, IReadOnlyList<BomLineInput> lines, string? note)
    {
        ctx.Require(Permissions.ProductionManage);
        if (lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Định mức chưa có nguyên vật liệu nào.");
        if (lines.Select(l => l.RawItemId).Distinct().Count() != lines.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi nguyên vật liệu chỉ có một dòng trong định mức.");
        if (lines.Any(l => l.QuantityPerUnit <= 0 || !MoneyMath.HasAtMostDecimals(l.QuantityPerUnit, MoneyMath.StorageScale)))
            throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng định mức phải lớn hơn 0 (tối đa 4 chữ số thập phân).");
        if (lines.Any(l => l.ScrapRate is < 0 or > 100 || !MoneyMath.HasAtMostDecimals(l.ScrapRate, 2)))
            throw new DomainException(ErrorCodes.ValidationFailed, "Tỷ lệ hao hụt từ 0 đến 100% (tối đa 2 chữ số thập phân).");
        if (lines.Any(l => l.RawItemId == itemId)) throw new DomainException(ErrorCodes.ValidationFailed, "Thành phẩm không thể là nguyên vật liệu của chính nó.");
        return await db.InTransactionAsync(async () =>
        {
            var ids = lines.Select(l => l.RawItemId).Append(itemId).Distinct().ToList();
            var found = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId && i.IsActive && ids.Contains(i.Id)).CountAsync();
            if (found != ids.Count) throw new DomainException(ErrorCodes.NotFound, "Có vật tư không tồn tại hoặc đã ngừng dùng.");
            var bom = await db.Boms.Include(b => b.Lines).FirstOrDefaultAsync(b => b.CompanyId == ctx.CompanyId && b.ItemId == itemId);
            if (bom is null)
            {
                bom = new Bom { Id = Ids.New(), CompanyId = ctx.CompanyId, ItemId = itemId, RowVersion = 1 };
                db.Boms.Add(bom);
            }
            else { db.BomLines.RemoveRange(bom.Lines); bom.RowVersion++; }
            bom.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(); bom.UpdatedAt = clock.UtcNow; bom.UpdatedBy = ctx.UserId;
            foreach (var l in lines)
                db.BomLines.Add(new BomLine { Id = Ids.New(), BomId = bom.Id, CompanyId = ctx.CompanyId, RawItemId = l.RawItemId, QuantityPerUnit = l.QuantityPerUnit, ScrapRate = l.ScrapRate });
            await audit.AppendAsync(ctx.CompanyId, ctx, "bom.save", "bom", bom.Id.ToString(), new { itemId, lines }, bom.RowVersion);
            await db.SaveChangesAsync();
            return await BomCoreAsync(ctx.CompanyId, itemId) ?? throw new InvalidOperationException();
        });
    }

    public async Task<IReadOnlyList<BomView>> BomsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var items = await db.Boms.AsNoTracking().Where(b => b.CompanyId == ctx.CompanyId).Select(b => b.ItemId).ToListAsync();
        var list = new List<BomView>();
        foreach (var id in items) list.Add((await BomCoreAsync(ctx.CompanyId, id))!);
        return list.OrderBy(b => b.ItemCode, StringComparer.Ordinal).ToList();
    }

    public async Task<WorkCenter> SaveWorkCenterAsync(SessionContext ctx, Guid? id, string code, string name, decimal laborRate, decimal overheadRate, bool isActive = true)
    {
        ctx.Require(Permissions.ProductionManage);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.ValidationFailed, "Mã và tên tổ xưởng bắt buộc.");
        if (laborRate < 0 || overheadRate < 0 || !MoneyMath.HasAtMostDecimals(laborRate, 4) || !MoneyMath.HasAtMostDecimals(overheadRate, 4))
            throw new DomainException(ErrorCodes.ValidationFailed, "Đơn giá theo giờ không âm (tối đa 4 chữ số thập phân).");
        return await db.InTransactionAsync(async () =>
        {
            code = code.Trim();
            WorkCenter w;
            if (id is null)
            {
                if (await db.WorkCenters.AnyAsync(x => x.CompanyId == ctx.CompanyId && x.Code == code)) throw new DomainException(ErrorCodes.DuplicateCode, $"Mã tổ xưởng {code} đã tồn tại.");
                w = new WorkCenter { Id = Ids.New(), CompanyId = ctx.CompanyId, Code = code, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1 };
                db.WorkCenters.Add(w);
            }
            else
            {
                w = await db.WorkCenters.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Tổ xưởng không tồn tại.");
                w.RowVersion++;
            }
            w.Name = name.Trim(); w.LaborRatePerHour = laborRate; w.OverheadRatePerHour = overheadRate; w.IsActive = isActive;
            await audit.AppendAsync(ctx.CompanyId, ctx, id is null ? "work_center.create" : "work_center.update", "work_center", w.Id.ToString(), new { w.Code, w.Name, laborRate, overheadRate, isActive }, w.RowVersion);
            await db.SaveChangesAsync();
            return w;
        });
    }

    public async Task<IReadOnlyList<WorkCenter>> WorkCentersAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        return await db.WorkCenters.AsNoTracking().Where(w => w.CompanyId == ctx.CompanyId).OrderBy(w => w.Code).ToListAsync();
    }

    // ------------------------------------------------------------ lệnh sản xuất

    public async Task<ProductionOrder> CreateAsync(SessionContext ctx, ProductionOrderInput input)
    {
        ctx.Require(Permissions.ProductionManage);
        if (input.PlannedQuantity <= 0 || !MoneyMath.HasAtMostDecimals(input.PlannedQuantity, MoneyMath.StorageScale))
            throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng kế hoạch phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            if (!await db.Boms.AnyAsync(b => b.CompanyId == ctx.CompanyId && b.ItemId == input.ItemId))
                throw new DomainException(ErrorCodes.ValidationFailed, "Thành phẩm chưa có định mức nguyên vật liệu; lập định mức trước.");
            var obj = await db.CostObjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CostObjectId && c.CompanyId == ctx.CompanyId && c.IsActive)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Đối tượng tính giá thành không tồn tại.");
            if (obj.ItemId != input.ItemId) throw new DomainException(ErrorCodes.ValidationFailed, "Đối tượng tính giá thành thuộc thành phẩm khác.");
            if (!await db.Warehouses.AnyAsync(w => w.Id == input.WarehouseId && w.CompanyId == ctx.CompanyId)) throw new DomainException(ErrorCodes.NotFound, "Kho không tồn tại.");
            var o = new ProductionOrder
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, ItemId = input.ItemId, CostObjectId = input.CostObjectId, WarehouseId = input.WarehouseId,
                PlannedQuantity = input.PlannedQuantity, PlannedStart = input.PlannedStart, Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
                CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1, Number = await NextNumberAsync(ctx.CompanyId, input.PlannedStart),
            };
            db.ProductionOrders.Add(o);
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.create", "production_order", o.Id.ToString(), new { o.Number, o.ItemId, o.PlannedQuantity }, o.RowVersion);
            await db.SaveChangesAsync();
            return o;
        });
    }

    public async Task<ProductionOrder> ReleaseAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.VoucherApprove);
        return await db.InTransactionAsync(async () =>
        {
            var o = await LoadAsync(ctx, id);
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            o.Release(ctx.UserId, clock.UtcNow, company.RequireDistinctApprover);
            o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.release", "production_order", o.Id.ToString(), new { o.Number }, o.RowVersion);
            await db.SaveChangesAsync();
            return o;
        });
    }

    public async Task<ProductionOrder> CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.ProductionManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy lệnh sản xuất phải có lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var o = await LoadAsync(ctx, id);
            if (o.Status is not (ProductionOrderStatus.Draft or ProductionOrderStatus.Released))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} đã phát sinh sản xuất hoặc đã hoàn thành, không hủy được.");
            if (await db.ProductionOrderIssues.AnyAsync(i => i.OrderId == id) || await db.ProductionConfirmations.AnyAsync(c => c.OrderId == id && c.VoidedAt == null))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} đã có phiếu xuất nguyên vật liệu hoặc báo công.");
            o.Status = ProductionOrderStatus.Cancelled; o.CancelReason = reason.Trim(); o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.cancel", "production_order", o.Id.ToString(), new { o.Number, reason }, o.RowVersion);
            await db.SaveChangesAsync();
            return o;
        });
    }

    /// <summary>Nhu cầu nguyên vật liệu của lệnh theo định mức (kể cả hao hụt cho phép), đã xuất và còn thiếu so với tồn kho (khâu lập kế hoạch và nhu cầu vật tư).</summary>
    public async Task<IReadOnlyList<RequirementRow>> RequirementsAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ReportView);
        var o = await LoadAsync(ctx, orderId, tracking: false);
        var bom = await db.Boms.AsNoTracking().Include(b => b.Lines).FirstAsync(b => b.CompanyId == ctx.CompanyId && b.ItemId == o.ItemId);
        var rawIds = bom.Lines.Select(l => l.RawItemId).ToList();
        var items = await db.Items.AsNoTracking().Where(i => rawIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var issued = await IssuedQuantitiesAsync(o.Id);
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.WarehouseId == o.WarehouseId && rawIds.Contains(m.ItemId)).Select(m => new { m.ItemId, m.Quantity }).ToListAsync();
        var onHand = moves.GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var qty = o.ActualQuantity ?? o.PlannedQuantity;
        return bom.Lines.Select(l =>
        {
            var required = StandardQty(l, qty);
            var done = issued.GetValueOrDefault(l.RawItemId);
            var remaining = Math.Max(0, required - done);
            var have = onHand.GetValueOrDefault(l.RawItemId);
            var it = items[l.RawItemId];
            return new RequirementRow(it.Id, it.Code, it.Name, it.Unit, required, done, remaining, have, Math.Max(0, remaining - have));
        }).OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lập phiếu xuất kho nguyên vật liệu Nháp cho lệnh: mặc định phần còn thiếu so với định mức, đối ứng TK dở dang của lệnh. Ghi sổ phiếu ở màn Kho.</summary>
    public async Task<StockDocument> CreateIssueAsync(SessionContext ctx, Guid orderId, IReadOnlyList<BomLineInput>? lines, DateOnly date)
    {
        ctx.Require(Permissions.ProductionManage);
        ctx.Require(Permissions.StockDocument);
        return await db.InTransactionAsync(async () =>
        {
            var o = await LoadAsync(ctx, orderId);
            if (o.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} chưa ban hành hoặc đã hoàn thành; không xuất nguyên vật liệu.");
            var wip = (await db.CostObjects.AsNoTracking().FirstAsync(c => c.Id == o.CostObjectId)).WipAccountId;
            var toIssue = lines is { Count: > 0 }
                ? lines.Select(l => (l.RawItemId, Qty: l.QuantityPerUnit)).ToList()
                : (await RequirementsAsync(ctx, orderId)).Where(r => r.Remaining > 0).Select(r => (r.RawItemId, Qty: r.Remaining)).ToList();
            if (toIssue.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Đã xuất đủ nguyên vật liệu theo định mức.");
            var bomItems = await db.BomLines.AsNoTracking().Where(l => l.CompanyId == ctx.CompanyId && db.Boms.Any(b => b.Id == l.BomId && b.ItemId == o.ItemId)).Select(l => l.RawItemId).ToListAsync();
            if (toIssue.Any(l => !bomItems.Contains(l.RawItemId))) throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ xuất nguyên vật liệu có trong định mức của thành phẩm.");
            var doc = await stock.CreateDocumentAsync(ctx, new StockDocumentInput(StockDocumentType.Issue, o.WarehouseId, date, date, $"Xuất nguyên vật liệu cho lệnh sản xuất {o.Number}",
                toIssue.Select(l => new StockLineInput(l.RawItemId, l.Qty, CounterAccountId: wip)).ToList()));
            db.ProductionOrderIssues.Add(new ProductionOrderIssue { Id = Ids.New(), CompanyId = ctx.CompanyId, OrderId = o.Id, StockDocumentId = doc.Id });
            if (o.Status == ProductionOrderStatus.Released) { o.Status = ProductionOrderStatus.InProgress; }
            o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.issue", "production_order", o.Id.ToString(), new { o.Number, doc = doc.Number }, o.RowVersion);
            await db.SaveChangesAsync();
            return doc;
        });
    }

    public async Task<ProductionConfirmation> ConfirmWorkAsync(SessionContext ctx, Guid orderId, Guid workCenterId, DateOnly date, decimal laborHours, decimal machineHours, string? note)
    {
        ctx.Require(Permissions.ProductionManage);
        if (laborHours < 0 || machineHours < 0 || laborHours + machineHours == 0 || !MoneyMath.HasAtMostDecimals(laborHours, 2) || !MoneyMath.HasAtMostDecimals(machineHours, 2))
            throw new DomainException(ErrorCodes.ValidationFailed, "Giờ nhân công, giờ máy không âm, tối đa 2 chữ số thập phân và phải có ít nhất một loại giờ.");
        return await db.InTransactionAsync(async () =>
        {
            var o = await LoadAsync(ctx, orderId);
            if (o.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} chưa ban hành hoặc đã hoàn thành; không báo công.");
            var w = await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workCenterId && x.CompanyId == ctx.CompanyId && x.IsActive)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Tổ xưởng không tồn tại hoặc đã ngừng dùng.");
            var dp = await DecimalsAsync(ctx.CompanyId);
            var c = new ProductionConfirmation
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, OrderId = o.Id, WorkCenterId = w.Id, WorkDate = date, LaborHours = laborHours, MachineHours = machineHours,
                LaborRate = w.LaborRatePerHour, OverheadRate = w.OverheadRatePerHour,
                LaborCost = Math.Round(laborHours * w.LaborRatePerHour, dp, MidpointRounding.AwayFromZero),
                OverheadCost = Math.Round(machineHours * w.OverheadRatePerHour, dp, MidpointRounding.AwayFromZero),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.ProductionConfirmations.Add(c);
            if (o.Status == ProductionOrderStatus.Released) o.Status = ProductionOrderStatus.InProgress;
            o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.confirm", "production_order", o.Id.ToString(), new { o.Number, w.Code, laborHours, machineHours, c.LaborCost, c.OverheadCost }, o.RowVersion);
            await db.SaveChangesAsync();
            return c;
        });
    }

    public async Task VoidConfirmationAsync(SessionContext ctx, Guid confirmationId, string reason)
    {
        ctx.Require(Permissions.ProductionManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy báo công phải có lý do.");
        await db.InTransactionAsync(async () =>
        {
            var c = await db.ProductionConfirmations.FirstOrDefaultAsync(x => x.Id == confirmationId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Báo công không tồn tại.");
            var o = await LoadAsync(ctx, c.OrderId);
            if (o.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProgress)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} đã hoàn thành; không hủy báo công.");
            if (c.VoidedAt != null) throw new DomainException(ErrorCodes.InvalidStateTransition, "Báo công đã hủy.");
            c.VoidedAt = clock.UtcNow; c.VoidReason = reason.Trim(); o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.void_confirmation", "production_order", o.Id.ToString(), new { o.Number, confirmation = c.Id, reason }, o.RowVersion);
            await db.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>Giá thành của lệnh: lệnh đã hoàn thành lấy số đã chốt, lệnh đang chạy tính tạm theo số liệu hiện có và số lượng kế hoạch.</summary>
    public async Task<CostSheet> CostSheetAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ReportView);
        var o = await LoadAsync(ctx, orderId, tracking: false);
        return await CostSheetCoreAsync(ctx.CompanyId, o, o.ActualQuantity ?? o.PlannedQuantity, o.ActualQuantity != null, requireCosted: false);
    }

    /// <summary>
    /// Hoàn thành lệnh với số lượng thực nhập: mọi phiếu xuất nguyên vật liệu đã ghi sổ (hoặc hủy) và đã có giá xuất; ghi Nhật ký phân bổ nhân công và sản xuất chung;
    /// lập phiếu nhập thành phẩm Nháp theo tổng giá thành của lệnh; chốt giá thành đơn vị = tổng giá thành ÷ số lượng thực nhập.
    /// </summary>
    public async Task<ProductionOrder> CompleteAsync(SessionContext ctx, Guid orderId, decimal actualQuantity, DateOnly date)
    {
        ctx.Require(Permissions.ProductionManage);
        ctx.Require(Permissions.VoucherPost);
        ctx.Require(Permissions.StockDocument);
        if (actualQuantity <= 0 || !MoneyMath.HasAtMostDecimals(actualQuantity, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng thành phẩm thực nhập phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            var o = await LoadAsync(ctx, orderId);
            if (o.Status != ProductionOrderStatus.InProgress) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} chưa có sản xuất phát sinh hoặc đã hoàn thành.");
            var drafts = await (from i in db.ProductionOrderIssues join d in db.StockDocuments on i.StockDocumentId equals d.Id
                                where i.OrderId == o.Id && d.Status == StockDocumentStatus.Draft select d.Number).ToListAsync();
            if (drafts.Count > 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Còn phiếu xuất nguyên vật liệu chưa ghi sổ: {string.Join(", ", drafts)}. Ghi sổ hoặc hủy trước khi hoàn thành lệnh.");
            var sheet = await CostSheetCoreAsync(ctx.CompanyId, o, actualQuantity, true, requireCosted: true);
            if (sheet.TotalCost <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Lệnh chưa có chi phí nào (chưa xuất nguyên vật liệu hoặc báo công).");
            var obj = await db.CostObjects.AsNoTracking().FirstAsync(c => c.Id == o.CostObjectId);

            if (sheet.LaborCost + sheet.OverheadCost > 0)
            {
                async Task<Guid> Role(string code) => (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == code))?.AccountId
                    ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {code} (phân bổ chi phí chế biến cho lệnh sản xuất).");
                var gl = new List<VoucherLineInput>();
                if (sheet.LaborCost > 0) gl.Add(new VoucherLineInput(await Role("PROD_LABOR_APPLIED"), Side.Credit, sheet.LaborCost, Description: $"Phân bổ nhân công {o.Number}"));
                if (sheet.OverheadCost > 0) gl.Add(new VoucherLineInput(await Role("PROD_OVERHEAD_APPLIED"), Side.Credit, sheet.OverheadCost, Description: $"Phân bổ máy, sản xuất chung {o.Number}"));
                gl.Insert(0, new VoucherLineInput(obj.WipAccountId, Side.Debit, sheet.LaborCost + sheet.OverheadCost, Description: $"Chi phí chế biến {o.Number}"));
                var v = await engine.PostSystemVoucherAsync(ctx, new VoucherInput(VoucherTypes.InventoryCosting, date, date,
                    $"Phân bổ chi phí chế biến lệnh sản xuất {o.Number}", gl, SourceSystem: AllocationSource, SourceId: o.Id.ToString()));
                o.AllocationVoucherId = v.Id;
            }
            var receipt = await stock.CreateDocumentAsync(ctx, new StockDocumentInput(StockDocumentType.Receipt, o.WarehouseId, date, date, $"Nhập thành phẩm lệnh sản xuất {o.Number}",
                [new StockLineInput(o.ItemId, actualQuantity, sheet.TotalCost, obj.WipAccountId)]));
            o.ReceiptDocumentId = receipt.Id;
            o.ActualQuantity = actualQuantity; o.CompletedDate = date;
            o.MaterialCost = sheet.MaterialCost; o.LaborCost = sheet.LaborCost; o.OverheadCost = sheet.OverheadCost; o.TotalCost = sheet.TotalCost;
            o.UnitCost = Math.Round(sheet.TotalCost / actualQuantity, 4, MidpointRounding.AwayFromZero);
            o.Status = ProductionOrderStatus.Completed; o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.complete", "production_order", o.Id.ToString(),
                new { o.Number, actualQuantity, sheet.MaterialCost, sheet.LaborCost, sheet.OverheadCost, sheet.TotalCost, o.UnitCost, receipt = receipt.Number }, o.RowVersion);
            await db.SaveChangesAsync();
            return o;
        });
    }

    /// <summary>Đóng lệnh sau khi phiếu nhập thành phẩm đã ghi sổ: khóa mọi thay đổi.</summary>
    public async Task<ProductionOrder> CloseAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ProductionManage);
        return await db.InTransactionAsync(async () =>
        {
            var o = await LoadAsync(ctx, orderId);
            if (o.Status != ProductionOrderStatus.Completed) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {o.Number} chưa hoàn thành hoặc đã đóng.");
            var receipt = await db.StockDocuments.AsNoTracking().FirstAsync(d => d.Id == o.ReceiptDocumentId);
            if (receipt.Status != StockDocumentStatus.Posted) throw new DomainException(ErrorCodes.ValidationFailed, $"Phiếu nhập thành phẩm {receipt.Number} chưa ghi sổ.");
            o.Status = ProductionOrderStatus.Closed; o.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production_order.close", "production_order", o.Id.ToString(), new { o.Number }, o.RowVersion);
            await db.SaveChangesAsync();
            return o;
        });
    }

    public async Task<IReadOnlyList<ProductionOrderRow>> ListAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId).OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync();
        var ids = orders.Select(o => o.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return orders.Select(o => new ProductionOrderRow(o.Id, o.Number, o.ItemId, items[o.ItemId].Code, items[o.ItemId].Name, items[o.ItemId].Unit, o.PlannedQuantity, o.ActualQuantity,
            o.PlannedStart, o.CompletedDate, o.Status, o.TotalCost, o.UnitCost, o.ReceiptDocumentId, o.Note)).ToList();
    }

    public async Task<IReadOnlyList<ConfirmationRow>> ConfirmationsAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ReportView);
        await LoadAsync(ctx, orderId, tracking: false);
        return await (from c in db.ProductionConfirmations.AsNoTracking() join w in db.WorkCenters on c.WorkCenterId equals w.Id
                      where c.OrderId == orderId orderby c.WorkDate, c.CreatedAt
                      select new ConfirmationRow(c.Id, c.WorkDate, w.Code, w.Name, c.LaborHours, c.MachineHours, c.LaborCost, c.OverheadCost, c.Note, c.VoidedAt != null, c.VoidReason)).ToListAsync();
    }

    public async Task<IReadOnlyList<OrderIssueRow>> IssuesAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ReportView);
        await LoadAsync(ctx, orderId, tracking: false);
        return await (from i in db.ProductionOrderIssues.AsNoTracking() join d in db.StockDocuments on i.StockDocumentId equals d.Id
                      where i.OrderId == orderId orderby d.DocumentDate, d.Number select new OrderIssueRow(d.Id, d.Number, d.Status, d.DocumentDate)).ToListAsync();
    }

    // ------------------------------------------------------------ internals

    private static decimal StandardQty(BomLine l, decimal outputQty) => Math.Round(l.QuantityPerUnit * (1 + l.ScrapRate / 100m) * outputQty, MoneyMath.StorageScale, MidpointRounding.AwayFromZero);

    private async Task<Dictionary<Guid, decimal>> IssuedQuantitiesAsync(Guid orderId)
    {
        var rows = await (from i in db.ProductionOrderIssues join d in db.StockDocuments on i.StockDocumentId equals d.Id
                          join l in db.StockDocumentLines on d.Id equals l.DocumentId
                          where i.OrderId == orderId && d.Status == StockDocumentStatus.Posted select new { l.ItemId, l.Quantity }).ToListAsync();
        return rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
    }

    private async Task<CostSheet> CostSheetCoreAsync(Guid companyId, ProductionOrder o, decimal quantity, bool isActual, bool requireCosted)
    {
        if (o.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Closed)
        {
            var frozen = await MaterialsAsync(companyId, o, quantity, requireCosted: false);
            var hours = await HoursAsync(o.Id);
            return new CostSheet(o.Id, o.Number, o.Status, quantity, true, o.MaterialCost, o.LaborCost, o.OverheadCost, o.TotalCost, o.UnitCost, frozen.Rows, hours.Labor, hours.Machine);
        }
        var m = await MaterialsAsync(companyId, o, quantity, requireCosted);
        var conf = await db.ProductionConfirmations.AsNoTracking().Where(c => c.OrderId == o.Id && c.VoidedAt == null).ToListAsync();
        var labor = conf.Sum(c => c.LaborCost); var overhead = conf.Sum(c => c.OverheadCost);
        var total = m.Total + labor + overhead;
        return new CostSheet(o.Id, o.Number, o.Status, quantity, isActual, m.Total, labor, overhead, total, quantity > 0 ? Math.Round(total / quantity, 4, MidpointRounding.AwayFromZero) : 0,
            m.Rows, conf.Sum(c => c.LaborHours), conf.Sum(c => c.MachineHours));
    }

    private async Task<(decimal Total, IReadOnlyList<MaterialUsageRow> Rows)> MaterialsAsync(Guid companyId, ProductionOrder o, decimal quantity, bool requireCosted)
    {
        var docs = await (from i in db.ProductionOrderIssues join d in db.StockDocuments on i.StockDocumentId equals d.Id
                          where i.OrderId == o.Id && d.Status == StockDocumentStatus.Posted select d.Id).ToListAsync();
        var moves = await db.StockMovements.AsNoTracking().Where(mv => docs.Contains(mv.DocumentId)).ToListAsync();
        var moveIds = moves.Select(mv => mv.Id).ToList();
        var vals = (await db.StockValuations.AsNoTracking().Where(v => v.VoidedAt == null && moveIds.Contains(v.MovementId)).ToListAsync())
            .GroupBy(v => v.MovementId).ToDictionary(g => g.Key, g => g.Sum(v => v.Value));
        var uncosted = moves.Count(mv => !vals.ContainsKey(mv.Id));
        if (requireCosted && uncosted > 0)
            throw new DomainException(ErrorCodes.ValidationFailed, $"{uncosted} dòng xuất nguyên vật liệu chưa được tính giá xuất (chạy tính giá cuối tháng ở màn Kho) nên chưa tính được giá thành.");
        var bom = await db.Boms.AsNoTracking().Include(b => b.Lines).FirstAsync(b => b.CompanyId == companyId && b.ItemId == o.ItemId);
        var rawIds = moves.Select(mv => mv.ItemId).Concat(bom.Lines.Select(l => l.RawItemId)).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => rawIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var rows = rawIds.Select(id =>
        {
            var line = bom.Lines.FirstOrDefault(l => l.RawItemId == id);
            var mine = moves.Where(mv => mv.ItemId == id).ToList();
            var issuedQty = mine.Sum(mv => Math.Abs(mv.Quantity));
            var cost = mine.Sum(mv => Math.Abs(vals.GetValueOrDefault(mv.Id)));
            var standard = line is null ? 0 : StandardQty(line, quantity);
            return new MaterialUsageRow(id, items[id].Code, items[id].Name, items[id].Unit, standard, issuedQty, issuedQty - standard, cost);
        }).OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
        return (rows.Sum(r => r.Cost), rows);
    }

    private async Task<(decimal Labor, decimal Machine)> HoursAsync(Guid orderId)
    {
        var c = await db.ProductionConfirmations.AsNoTracking().Where(x => x.OrderId == orderId && x.VoidedAt == null).ToListAsync();
        return (c.Sum(x => x.LaborHours), c.Sum(x => x.MachineHours));
    }

    private async Task<BomView?> BomCoreAsync(Guid companyId, Guid itemId)
    {
        var bom = await db.Boms.AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ItemId == itemId);
        if (bom is null) return null;
        var ids = bom.Lines.Select(l => l.RawItemId).Append(itemId).ToList();
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return new BomView(bom.Id, itemId, items[itemId].Code, items[itemId].Name, bom.Note,
            bom.Lines.Select(l => new BomLineView(l.RawItemId, items[l.RawItemId].Code, items[l.RawItemId].Name, items[l.RawItemId].Unit, l.QuantityPerUnit, l.ScrapRate))
                .OrderBy(l => l.RawCode, StringComparer.Ordinal).ToList());
    }

    private async Task<ProductionOrder> LoadAsync(SessionContext ctx, Guid id, bool tracking = true)
    {
        var q = tracking ? db.ProductionOrders : db.ProductionOrders.AsNoTracking();
        return await q.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Lệnh sản xuất không tồn tại.");
    }

    private async Task<int> DecimalsAsync(Guid companyId)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        return await db.Currencies.Where(c => c.Code == company.FunctionalCurrency).Select(c => c.DecimalPlaces).FirstAsync();
    }

    private async Task<string> NextNumberAsync(Guid companyId, DateOnly date)
    {
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
                 ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date.Vn()}.");
        const string key = "PROD-LSX";
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++;
        seq.RowVersion++;
        return $"LSX{fy.Code}-{seq.LastNumber:D6}";
    }
}
