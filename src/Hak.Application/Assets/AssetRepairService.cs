using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Assets;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Assets;

public sealed record RepairMaterialInput(Guid ItemId, decimal Quantity, decimal UnitPrice);

public sealed record AssetRepairInput(Guid AssetId, RepairType Type, DateOnly ReportedDate, string Description, Guid? RequestId = null, Guid? ScheduleId = null, string? Vendor = null,
    string? AssignedTech = null, string? RootCause = null, string? SolutionPlan = null, decimal EstimatedLaborCost = 0, IReadOnlyList<RepairMaterialInput>? Materials = null);

public sealed record RepairMaterialView(Guid Id, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal QtyRequested, decimal QtyIssued, decimal UnitPrice, RepairMaterialStatus Status, string? StockDocumentNumber);

public sealed record AssetRepairView(Guid Id, string Number, Guid AssetId, string AssetCode, string AssetName, RepairType Type, RepairStatus Status, DateOnly ReportedDate, string Description,
    string? RequestNumber, string? Vendor, string? AssignedTech, string? RootCause, string? SolutionPlan, decimal EstimatedLaborCost, decimal EstimatedMaterialCost, decimal EstimatedTotal,
    decimal? ActualLaborCost, decimal ActualMaterialCost, decimal ActualTotal, DateTime? StartTime, DateTime? EndTime, string? Note, IReadOnlyList<RepairMaterialView> Materials);

public sealed record MaintenanceRequestView(Guid Id, string Number, Guid AssetId, string AssetCode, string AssetName, MaintenancePriority Priority, string Description, DateTime ReportedAt,
    MaintenanceRequestStatus Status, string? Note, string? WorkOrderNumber);

public sealed record PreventiveScheduleView(Guid Id, Guid AssetId, string AssetCode, string AssetName, string TaskName, int FrequencyDays, DateOnly? LastPerformedAt, DateOnly NextDueDate, bool IsActive,
    int DaysToDue, bool Overdue);

public sealed record MaintenanceKpiRow(Guid AssetId, string AssetCode, string AssetName, string? Department, int Failures, decimal RepairHours, decimal? Mttr, decimal? Mtbf, decimal DowntimeRate,
    decimal LaborCost, decimal MaterialCost, decimal TotalCost, int PreventiveCount);

/// <summary>
/// Quản lý bảo trì &amp; sửa chữa tài sản (theo tệp phân tích CMMS chị gửi): yêu cầu sửa chữa (YCSC) → khảo sát, phương án và dự toán → duyệt → thi công (xuất vật tư) → nghiệm thu → đóng;
/// lịch bảo trì phòng ngừa; KPI MTBF, MTTR, tỷ lệ dừng máy và chi phí theo máy. Không ghi Nhật ký: vật tư xuất bằng phiếu xuất kho (ghi sổ ở màn Kho), chi phí khác bằng hóa đơn / phiếu chi.
/// </summary>
public sealed class AssetRepairService(HakDbContext db, StockService stock, AuditService audit, IClock clock)
{
    // ------------------------------------------------------------ yêu cầu sửa chữa (YCSC)

    public async Task<MaintenanceRequest> RequestAsync(SessionContext ctx, Guid assetId, MaintenancePriority priority, string description)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException(ErrorCodes.ValidationFailed, "Mô tả hiện tượng hư hỏng bắt buộc.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await ActiveAssetAsync(ctx, assetId);
            var r = new MaintenanceRequest { Id = Ids.New(), CompanyId = ctx.CompanyId, AssetId = a.Id, Priority = priority, Description = description.Trim(), ReportedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Number = await NextNumberAsync(ctx.CompanyId, DateOnly.FromDateTime(clock.UtcNow), "YCSC") };
            db.MaintenanceRequests.Add(r);
            await audit.AppendAsync(ctx.CompanyId, ctx, "maintenance_request.create", "asset", a.Id.ToString(), new { r.Number, a.Code, priority }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<IReadOnlyList<MaintenanceRequestView>> RequestsAsync(SessionContext ctx, Guid? assetId = null)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.MaintenanceRequests.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId);
        if (assetId != null) q = q.Where(r => r.AssetId == assetId);
        var rows = await q.OrderByDescending(r => r.ReportedAt).Take(300).ToListAsync();
        var assets = await AssetsAsync(rows.Select(r => r.AssetId));
        var ids = rows.Select(r => r.Id).ToList();
        var wos = (await db.AssetRepairs.AsNoTracking().Where(w => w.RequestId != null && ids.Contains(w.RequestId.Value) && w.Status != RepairStatus.Cancelled).ToListAsync())
            .GroupBy(w => w.RequestId!.Value).ToDictionary(g => g.Key, g => g.First().Number);
        return rows.Select(r => new MaintenanceRequestView(r.Id, r.Number, r.AssetId, assets[r.AssetId].Code, assets[r.AssetId].Name, r.Priority, r.Description, r.ReportedAt, r.Status, r.Note, wos.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<MaintenanceRequest> RejectRequestAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.VoucherApprove);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối yêu cầu phải có lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var r = await db.MaintenanceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Yêu cầu sửa chữa không tồn tại.");
            if (r.Status is not (MaintenanceRequestStatus.New or MaintenanceRequestStatus.Surveying)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Yêu cầu {r.Number} đã có phương án, không từ chối trực tiếp.");
            r.Status = MaintenanceRequestStatus.Rejected; r.Note = reason.Trim(); r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "maintenance_request.reject", "asset", r.AssetId.ToString(), new { r.Number, reason }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    // ------------------------------------------------------------ phiếu thi công

    public async Task<AssetRepair> CreateAsync(SessionContext ctx, AssetRepairInput input)
    {
        ctx.Require(Permissions.AssetManage);
        Validate(input);
        return await db.InTransactionAsync(async () =>
        {
            var a = await ActiveAssetAsync(ctx, input.AssetId);
            var r = new AssetRepair { Id = Ids.New(), CompanyId = ctx.CompanyId, AssetId = a.Id, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Number = await NextNumberAsync(ctx.CompanyId, input.ReportedDate, "SCTS") };
            if (input.RequestId is { } rid)
            {
                var req = await db.MaintenanceRequests.FirstOrDefaultAsync(x => x.Id == rid && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Yêu cầu sửa chữa không tồn tại.");
                if (req.AssetId != a.Id) throw new DomainException(ErrorCodes.ValidationFailed, "Yêu cầu thuộc tài sản khác.");
                if (req.Status is not (MaintenanceRequestStatus.New or MaintenanceRequestStatus.Surveying)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Yêu cầu {req.Number} đã có phương án hoặc đã xử lý.");
                req.Status = MaintenanceRequestStatus.Surveying; req.RowVersion++;
            }
            if (input.ScheduleId is { } sid && !await db.PreventiveSchedules.AnyAsync(x => x.Id == sid && x.CompanyId == ctx.CompanyId && x.AssetId == a.Id))
                throw new DomainException(ErrorCodes.NotFound, "Lịch bảo trì không tồn tại hoặc thuộc tài sản khác.");
            db.AssetRepairs.Add(r);
            await ApplyAsync(ctx, r, input, isNew: true);
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_repair.create", "asset", a.Id.ToString(), new { r.Number, a.Code, input.Type, r.EstimatedLaborCost, r.EstimatedMaterialCost }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    /// <summary>Sửa phương án và dự toán khi phiếu còn Nháp (khảo sát xong thì ghi nguyên nhân, phương án, vật tư).</summary>
    public async Task<AssetRepair> UpdatePlanAsync(SessionContext ctx, Guid id, AssetRepairInput input)
    {
        ctx.Require(Permissions.AssetManage);
        Validate(input);
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadAsync(ctx, id);
            if (r.Status != RepairStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {r.Number} đã gửi duyệt, không sửa phương án.");
            foreach (var m in r.Materials.ToList()) db.AssetRepairMaterials.Remove(m);
            r.Materials.Clear();
            await ApplyAsync(ctx, r, input with { AssetId = r.AssetId, RequestId = r.RequestId, ScheduleId = r.ScheduleId }, isNew: false);
            r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_repair.update_plan", "asset", r.AssetId.ToString(), new { r.Number, r.EstimatedLaborCost, r.EstimatedMaterialCost }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public Task<AssetRepair> SubmitAsync(SessionContext ctx, Guid id) => Transition(ctx, id, Permissions.AssetManage, "asset_repair.submit", async (r, _) =>
    {
        if (string.IsNullOrWhiteSpace(r.SolutionPlan)) throw new DomainException(ErrorCodes.ValidationFailed, "Chưa có phương án xử lý để gửi duyệt.");
        r.Move(RepairStatus.Draft, RepairStatus.PendingApproval);
        await SetRequestAsync(r, MaintenanceRequestStatus.Proposed);
    });

    /// <summary>Duyệt hoặc từ chối phương án và dự toán. Người duyệt khác người lập khi công ty bắt buộc tách người; từ chối cần lý do.</summary>
    public Task<AssetRepair> DecideAsync(SessionContext ctx, Guid id, bool approve, string? note) => Transition(ctx, id, Permissions.VoucherApprove, approve ? "asset_repair.approve" : "asset_repair.reject", async (r, company) =>
    {
        if (r.Status != RepairStatus.PendingApproval) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {r.Number} không ở trạng thái chờ duyệt.");
        if (company.RequireDistinctApprover && r.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt phương án sửa chữa không được trùng người lập phiếu.");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối phương án phải có lý do.");
        r.Move(RepairStatus.PendingApproval, approve ? RepairStatus.Approved : RepairStatus.Rejected);
        r.ApprovedBy = ctx.UserId; r.ApprovedAt = clock.UtcNow; if (!string.IsNullOrWhiteSpace(note)) r.Note = note.Trim();
        await SetRequestAsync(r, approve ? MaintenanceRequestStatus.Approved : MaintenanceRequestStatus.Rejected);
    });

    public Task<AssetRepair> StartAsync(SessionContext ctx, Guid id) => Transition(ctx, id, Permissions.AssetManage, "asset_repair.start", async (r, _) =>
    {
        r.Move(RepairStatus.Approved, RepairStatus.InProgress); r.StartTime = clock.UtcNow;
        await SetRequestAsync(r, MaintenanceRequestStatus.InProgress);
    });

    /// <summary>Lập phiếu xuất kho Nháp cho các vật tư đã đề xuất (đối ứng TK chi phí chọn khi xuất). Ghi sổ phiếu ở màn Kho.</summary>
    public async Task<StockDocument> IssueMaterialsAsync(SessionContext ctx, Guid id, Guid warehouseId, Guid counterAccountId, DateOnly date)
    {
        ctx.Require(Permissions.AssetManage);
        ctx.Require(Permissions.StockDocument);
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadAsync(ctx, id);
            if (r.Status != RepairStatus.InProgress) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {r.Number} chưa thi công hoặc đã chuyển nghiệm thu; không xuất vật tư.");
            var pending = r.Materials.Where(m => m.Status == RepairMaterialStatus.Pending).ToList();
            if (pending.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không còn vật tư chờ xuất.");
            var doc = await stock.CreateDocumentAsync(ctx, new StockDocumentInput(StockDocumentType.Issue, warehouseId, date, date, $"Xuất vật tư sửa chữa theo phiếu {r.Number}",
                pending.Select(m => new StockLineInput(m.ItemId, m.QtyRequested, CounterAccountId: counterAccountId)).ToList()));
            foreach (var m in pending) { m.Status = RepairMaterialStatus.Issued; m.QtyIssued = m.QtyRequested; m.StockDocumentId = doc.Id; }
            r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_repair.issue", "asset", r.AssetId.ToString(), new { r.Number, doc = doc.Number }, r.RowVersion);
            await db.SaveChangesAsync();
            return doc;
        });
    }

    /// <summary>Thi công xong, yêu cầu nghiệm thu: ghi chi phí nhân công thực tế và thời điểm hoàn thành.</summary>
    public async Task<AssetRepair> RequestAcceptanceAsync(SessionContext ctx, Guid id, decimal actualLaborCost, string? note)
    {
        if (actualLaborCost < 0 || !MoneyMath.HasAtMostDecimals(actualLaborCost, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.ValidationFailed, "Chi phí nhân công thực tế không hợp lệ.");
        return await Transition(ctx, id, Permissions.AssetManage, "asset_repair.test", (r, _) =>
        {
            if (r.Materials.Any(m => m.Status == RepairMaterialStatus.Pending)) throw new DomainException(ErrorCodes.ValidationFailed, "Còn vật tư chờ xuất kho; xuất hoặc bỏ trước khi nghiệm thu.");
            r.Move(RepairStatus.InProgress, RepairStatus.Testing); r.EndTime = clock.UtcNow; r.ActualLaborCost = actualLaborCost;
            if (!string.IsNullOrWhiteSpace(note)) r.Note = note.Trim();
            return Task.CompletedTask;
        });
    }

    /// <summary>Nghiệm thu đạt (Testing → Completed); không đạt thì trả về thi công.</summary>
    public Task<AssetRepair> AcceptAsync(SessionContext ctx, Guid id, bool passed, string? note) => Transition(ctx, id, Permissions.AssetManage, passed ? "asset_repair.accept" : "asset_repair.reject_test", (r, _) =>
    {
        if (!passed && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Nghiệm thu không đạt phải ghi lý do.");
        if (passed) r.Move(RepairStatus.Testing, RepairStatus.Completed);
        else { r.Move(RepairStatus.Testing, RepairStatus.InProgress); r.EndTime = null; r.Note = note!.Trim(); }
        return Task.CompletedTask;
    });

    /// <summary>Đóng phiếu (người có quyền duyệt): yêu cầu gốc hoàn tất; phiếu bảo trì định kỳ cập nhật ngày thực hiện và hạn kế tiếp.</summary>
    public Task<AssetRepair> CloseAsync(SessionContext ctx, Guid id) => Transition(ctx, id, Permissions.VoucherApprove, "asset_repair.close", async (r, _) =>
    {
        r.Move(RepairStatus.Completed, RepairStatus.Closed); r.ClosedBy = ctx.UserId; r.ClosedAt = clock.UtcNow;
        await SetRequestAsync(r, MaintenanceRequestStatus.Done);
        if (r.ScheduleId is { } sid)
        {
            var s = await db.PreventiveSchedules.FirstAsync(x => x.Id == sid);
            var done = DateOnly.FromDateTime(r.EndTime ?? clock.UtcNow);
            s.LastPerformedAt = done; s.NextDueDate = done.AddDays(s.FrequencyDays); s.RowVersion++;
        }
    });

    public async Task<AssetRepair> CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy phiếu sửa chữa phải có lý do.");
        return await Transition(ctx, id, Permissions.AssetManage, "asset_repair.cancel", async (r, _) =>
        {
            if (r.Status is not (RepairStatus.Draft or RepairStatus.PendingApproval or RepairStatus.Approved or RepairStatus.InProgress))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {r.Number} không hủy được ở trạng thái hiện tại.");
            if (r.Materials.Any(m => m.Status == RepairMaterialStatus.Issued)) throw new DomainException(ErrorCodes.InvalidStateTransition, "Đã lập phiếu xuất kho vật tư; hủy phiếu xuất kho trước.");
            r.Status = RepairStatus.Cancelled; r.Note = reason.Trim(); r.RowVersion++;
            await SetRequestAsync(r, MaintenanceRequestStatus.New);
        });
    }

    public async Task<IReadOnlyList<AssetRepairView>> ListAsync(SessionContext ctx, Guid? assetId = null)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.AssetRepairs.AsNoTracking().Include(r => r.Materials).Where(r => r.CompanyId == ctx.CompanyId);
        if (assetId != null) q = q.Where(r => r.AssetId == assetId);
        var rows = await q.OrderByDescending(r => r.ReportedDate).ThenByDescending(r => r.Number).Take(300).ToListAsync();
        var assets = await AssetsAsync(rows.Select(r => r.AssetId));
        var itemIds = rows.SelectMany(r => r.Materials).Select(m => m.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var docIds = rows.SelectMany(r => r.Materials).Where(m => m.StockDocumentId != null).Select(m => m.StockDocumentId!.Value).Distinct().ToList();
        var docs = await db.StockDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number);
        var reqIds = rows.Where(r => r.RequestId != null).Select(r => r.RequestId!.Value).ToList();
        var reqs = await db.MaintenanceRequests.AsNoTracking().Where(x => reqIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number);
        return rows.Select(r => new AssetRepairView(r.Id, r.Number, r.AssetId, assets[r.AssetId].Code, assets[r.AssetId].Name, r.Type, r.Status, r.ReportedDate, r.Description,
            r.RequestId is { } q2 ? reqs.GetValueOrDefault(q2) : null, r.Vendor, r.AssignedTech, r.RootCause, r.SolutionPlan, r.EstimatedLaborCost, r.EstimatedMaterialCost, r.EstimatedTotal,
            r.ActualLaborCost, r.ActualMaterialCost, r.ActualTotal, r.StartTime, r.EndTime, r.Note,
            r.Materials.Select(m => new RepairMaterialView(m.Id, m.ItemId, items[m.ItemId].Code, items[m.ItemId].Name, items[m.ItemId].Unit, m.QtyRequested, m.QtyIssued, m.UnitPrice, m.Status,
                m.StockDocumentId is { } d ? docs.GetValueOrDefault(d) : null)).ToList())).ToList();
    }

    // ------------------------------------------------------------ lịch bảo trì phòng ngừa

    public async Task<PreventiveSchedule> SaveScheduleAsync(SessionContext ctx, Guid? id, Guid assetId, string taskName, int frequencyDays, DateOnly nextDue, bool isActive = true)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(taskName) || frequencyDays <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Tên hạng mục và chu kỳ (ngày) bắt buộc, chu kỳ phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await ActiveAssetAsync(ctx, assetId);
            PreventiveSchedule s;
            if (id is { } sid)
            {
                s = await db.PreventiveSchedules.FirstOrDefaultAsync(x => x.Id == sid && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Lịch bảo trì không tồn tại.");
                s.TaskName = taskName.Trim(); s.FrequencyDays = frequencyDays; s.NextDueDate = nextDue; s.IsActive = isActive; s.RowVersion++;
            }
            else db.PreventiveSchedules.Add(s = new PreventiveSchedule { Id = Ids.New(), CompanyId = ctx.CompanyId, AssetId = a.Id, TaskName = taskName.Trim(), FrequencyDays = frequencyDays, NextDueDate = nextDue, IsActive = isActive, RowVersion = 1 });
            await audit.AppendAsync(ctx.CompanyId, ctx, "preventive_schedule.save", "asset", a.Id.ToString(), new { s.TaskName, frequencyDays, nextDue, isActive }, s.RowVersion);
            await db.SaveChangesAsync();
            return s;
        });
    }

    public async Task<IReadOnlyList<PreventiveScheduleView>> SchedulesAsync(SessionContext ctx, DateOnly today)
    {
        ctx.Require(Permissions.ReportView);
        var rows = await db.PreventiveSchedules.AsNoTracking().Where(s => s.CompanyId == ctx.CompanyId).OrderBy(s => s.NextDueDate).ToListAsync();
        var assets = await AssetsAsync(rows.Select(r => r.AssetId));
        return rows.Select(s => new PreventiveScheduleView(s.Id, s.AssetId, assets[s.AssetId].Code, assets[s.AssetId].Name, s.TaskName, s.FrequencyDays, s.LastPerformedAt, s.NextDueDate, s.IsActive,
            s.NextDueDate.DayNumber - today.DayNumber, s.IsActive && s.NextDueDate < today)).ToList();
    }

    /// <summary>Lập phiếu bảo trì Nháp từ lịch đến hạn (mỗi lịch chỉ có một phiếu đang mở).</summary>
    public async Task<AssetRepair> CreateFromScheduleAsync(SessionContext ctx, Guid scheduleId, DateOnly date)
    {
        var s = await db.PreventiveSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scheduleId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Lịch bảo trì không tồn tại.");
        if (!s.IsActive) throw new DomainException(ErrorCodes.InvalidStateTransition, "Lịch bảo trì đã tắt.");
        if (await db.AssetRepairs.AnyAsync(r => r.ScheduleId == scheduleId && r.Status != RepairStatus.Closed && r.Status != RepairStatus.Cancelled && r.Status != RepairStatus.Rejected))
            throw new DomainException(ErrorCodes.DuplicateSource, "Lịch này đã có phiếu bảo trì đang xử lý.");
        return await CreateAsync(ctx, new AssetRepairInput(s.AssetId, RepairType.Preventive, date, s.TaskName, ScheduleId: s.Id, SolutionPlan: s.TaskName));
    }

    // ------------------------------------------------------------ KPI

    /// <summary>
    /// MTBF = (thời gian kế hoạch − thời gian dừng) ÷ số sự cố; MTTR = tổng giờ sửa ÷ số sự cố; tỷ lệ dừng máy = giờ dừng ÷ giờ kế hoạch.
    /// Sự cố = phiếu sửa chữa khẩn cấp đã nghiệm thu (có giờ bắt đầu và giờ kết thúc) trong kỳ; thời gian kế hoạch = 24 giờ × số ngày trong kỳ. Chi phí tính các phiếu đã nghiệm thu hoặc đóng.
    /// </summary>
    public async Task<IReadOnlyList<MaintenanceKpiRow>> KpiAsync(SessionContext ctx, DateOnly from, DateOnly to)
    {
        ctx.Require(Permissions.ReportView);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var done = await db.AssetRepairs.AsNoTracking().Include(r => r.Materials).Where(r => r.CompanyId == ctx.CompanyId && (r.Status == RepairStatus.Completed || r.Status == RepairStatus.Closed)
            && r.EndTime != null && r.EndTime >= start && r.EndTime < end).ToListAsync();
        var assets = await AssetsAsync(done.Select(r => r.AssetId));
        var dept = (await db.AssetAssignments.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && x.EndDate == null).ToListAsync()).ToDictionary(x => x.AssetId, x => x.Department);
        var planned = (decimal)(to.DayNumber - from.DayNumber + 1) * 24;
        return done.GroupBy(r => r.AssetId).Select(g =>
        {
            var failures = g.Where(r => r.Type == RepairType.Emergency && r.StartTime != null).ToList();
            var hours = failures.Sum(r => (decimal)(r.EndTime!.Value - r.StartTime!.Value).TotalHours);
            var n = failures.Count;
            var a = assets[g.Key];
            return new MaintenanceKpiRow(a.Id, a.Code, a.Name, dept.GetValueOrDefault(a.Id), n, Math.Round(hours, 2), n == 0 ? null : Math.Round(hours / n, 2),
                n == 0 ? null : Math.Round((planned - hours) / n, 2), Math.Round(hours / planned * 100, 2), g.Sum(r => r.ActualLaborCost ?? 0), g.Sum(r => r.ActualMaterialCost),
                g.Sum(r => r.ActualTotal), g.Count(r => r.Type == RepairType.Preventive));
        }).OrderByDescending(r => r.TotalCost).ToList();
    }

    // ------------------------------------------------------------ internals

    private async Task<AssetRepair> Transition(SessionContext ctx, Guid id, string permission, string action, Func<AssetRepair, Company, Task> change)
    {
        ctx.Require(permission);
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadAsync(ctx, id);
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            await change(r, company);
            r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, action, "asset", r.AssetId.ToString(), new { r.Number, status = r.Status.ToString() }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    private async Task ApplyAsync(SessionContext ctx, AssetRepair r, AssetRepairInput input, bool isNew)
    {
        var mats = input.Materials ?? [];
        var ids = mats.Select(m => m.ItemId).ToList();
        if (ids.Distinct().Count() != ids.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi vật tư chỉ một dòng.");
        if (await db.Items.CountAsync(i => i.CompanyId == ctx.CompanyId && ids.Contains(i.Id)) != ids.Count) throw new DomainException(ErrorCodes.NotFound, "Vật tư không tồn tại.");
        if (isNew)
        {
            r.Type = input.Type; r.ReportedDate = input.ReportedDate; r.RequestId = input.RequestId; r.ScheduleId = input.ScheduleId;
        }
        r.Description = input.Description.Trim(); r.Vendor = Blank(input.Vendor); r.AssignedTech = Blank(input.AssignedTech); r.RootCause = Blank(input.RootCause); r.SolutionPlan = Blank(input.SolutionPlan);
        r.EstimatedLaborCost = input.EstimatedLaborCost;
        r.EstimatedMaterialCost = mats.Sum(m => m.Quantity * m.UnitPrice);
        foreach (var m in mats)
            db.AssetRepairMaterials.Add(new AssetRepairMaterial { Id = Ids.New(), CompanyId = ctx.CompanyId, RepairId = r.Id, ItemId = m.ItemId, QtyRequested = m.Quantity, UnitPrice = m.UnitPrice });
    }

    private static void Validate(AssetRepairInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Description)) throw new DomainException(ErrorCodes.ValidationFailed, "Nội dung sự cố / công việc bảo trì bắt buộc.");
        if (i.EstimatedLaborCost < 0 || !MoneyMath.HasAtMostDecimals(i.EstimatedLaborCost, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.ValidationFailed, "Dự toán nhân công không hợp lệ.");
        if (i.Materials?.Any(m => m.Quantity <= 0 || m.UnitPrice < 0) == true) throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng vật tư phải lớn hơn 0 và đơn giá không âm.");
    }

    private async Task SetRequestAsync(AssetRepair r, MaintenanceRequestStatus status)
    {
        if (r.RequestId is not { } rid) return;
        var q = await db.MaintenanceRequests.FirstAsync(x => x.Id == rid);
        q.Status = status; q.RowVersion++;
    }

    private async Task<AssetRepair> LoadAsync(SessionContext ctx, Guid id)
        => await db.AssetRepairs.Include(r => r.Materials).FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu sửa chữa không tồn tại.");

    private async Task<LongTermAsset> ActiveAssetAsync(SessionContext ctx, Guid assetId)
    {
        var a = await db.LongTermAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thẻ tài sản không tồn tại.");
        if (a.Status != AssetStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Chỉ lập phiếu cho tài sản đang sử dụng; {a.Code} đang ở trạng thái {a.Status}.");
        return a;
    }

    private async Task<Dictionary<Guid, LongTermAsset>> AssetsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.LongTermAssets.AsNoTracking().Where(a => list.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
    }

    private async Task<string> NextNumberAsync(Guid companyId, DateOnly date, string prefix)
    {
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
                 ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date.Vn()}.");
        var key = "AST-" + prefix;
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++; seq.RowVersion++;
        return $"{prefix}{fy.Code}-{seq.LastNumber:D6}";
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
