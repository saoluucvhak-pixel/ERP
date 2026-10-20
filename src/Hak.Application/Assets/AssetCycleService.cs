using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Assets;
using Hak.Domain.Common;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Assets;

public sealed record AssetRequestInput(AssetClass Class, string Name, string? Department, decimal EstimatedCost, DateOnly NeededDate, string Reason);

public sealed record AssetCurrentAssignment(Guid AssetId, string Code, string Name, AssetClass Class, string? Department, string? Custodian, string? Location, DateOnly? Since);

public sealed record AssetCountLineInput(Guid AssetId, AssetCountResult Result, string? ActualDepartment = null, string? Note = null);

public sealed record AssetCountLineView(Guid AssetId, string Code, string Name, AssetClass Class, string? BookDepartment, AssetCountResult Result, string? ActualDepartment, string? Note, decimal NetBookValue);

public sealed record AssetCountView(Guid Id, string Number, DateOnly CountDate, string? Note, AssetCountStatus Status, int Total, int Counted, int Missing, int Damaged, int Moved,
    decimal MissingNetBookValue, IReadOnlyList<AssetCountLineView> Lines);

/// <summary>
/// Chu trình quản lý tài sản cố định / công cụ dụng cụ (tệp phân tích ERP: mua sắm TSCĐ thuộc chu trình tài chính, "quản lý tài sản cố định" là phân hệ ERP,
/// kiểm kê tài sản là kiểm soát phát hiện): đề nghị mua sắm (duyệt, không ghi Nhật ký) → mua qua chu trình cung ứng → lập thẻ và ghi tăng (ghi Nhật ký, AssetService)
/// → bàn giao cho bộ phận / người sử dụng → khấu hao, phân bổ hằng kỳ (ghi Nhật ký) → kiểm kê định kỳ (không ghi Nhật ký) → ghi giảm / thanh lý (ghi Nhật ký).
/// </summary>
public sealed class AssetCycleService(HakDbContext db, AuditService audit, IClock clock)
{
    // ------------------------------------------------------------ đề nghị mua sắm

    public async Task<AssetRequest> RequestAsync(SessionContext ctx, AssetRequestInput input)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên tài sản và lý do đề nghị bắt buộc.");
        if (input.EstimatedCost <= 0 || !MoneyMath.HasAtMostDecimals(input.EstimatedCost, MoneyMath.StorageScale))
            throw new DomainException(ErrorCodes.AmountNotPositive, "Giá trị dự kiến phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            var r = new AssetRequest
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Class = input.Class, Name = input.Name.Trim(), Department = NullIfBlank(input.Department), EstimatedCost = input.EstimatedCost,
                NeededDate = input.NeededDate, Reason = input.Reason.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Number = await NextNumberAsync(ctx.CompanyId, input.NeededDate, "DNMS"),
            };
            db.AssetRequests.Add(r);
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_request.create", "asset_request", r.Id.ToString(), new { r.Number, r.Name, r.EstimatedCost }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<AssetRequest> DecideAsync(SessionContext ctx, Guid id, bool approve, string? note)
    {
        ctx.Require(Permissions.VoucherApprove);
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadRequestAsync(ctx, id);
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            r.Decide(approve, ctx.UserId, clock.UtcNow, note, company.RequireDistinctApprover);
            r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, approve ? "asset_request.approve" : "asset_request.reject", "asset_request", r.Id.ToString(), new { r.Number, note }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<AssetRequest> CancelRequestAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy đề nghị phải có lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadRequestAsync(ctx, id);
            if (r.Status is not (AssetRequestStatus.Pending or AssetRequestStatus.Approved))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đề nghị {r.Number} không hủy được ở trạng thái hiện tại.");
            r.Status = AssetRequestStatus.Cancelled; r.DecisionNote = reason.Trim(); r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_request.cancel", "asset_request", r.Id.ToString(), new { r.Number, reason }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    /// <summary>Gắn thẻ tài sản đã lập (từ hóa đơn mua) vào đề nghị đã duyệt; đề nghị chuyển sang "Đã thực hiện".</summary>
    public async Task<AssetRequest> LinkAssetAsync(SessionContext ctx, Guid requestId, Guid assetId)
    {
        ctx.Require(Permissions.AssetManage);
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadRequestAsync(ctx, requestId);
            if (r.Status != AssetRequestStatus.Approved) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đề nghị {r.Number} chưa được duyệt hoặc đã xử lý.");
            var a = await db.LongTermAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Thẻ tài sản không tồn tại.");
            if (a.Class != r.Class) throw new DomainException(ErrorCodes.ValidationFailed, "Loại tài sản của thẻ khác loại trong đề nghị.");
            if (a.Status == AssetStatus.Cancelled) throw new DomainException(ErrorCodes.ValidationFailed, "Thẻ tài sản đã hủy.");
            if (await db.AssetRequests.AnyAsync(x => x.AssetId == assetId)) throw new DomainException(ErrorCodes.DuplicateSource, "Thẻ tài sản đã gắn vào đề nghị khác.");
            r.AssetId = assetId; r.Status = AssetRequestStatus.Fulfilled; r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_request.fulfil", "asset_request", r.Id.ToString(), new { r.Number, asset = a.Code }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<IReadOnlyList<AssetRequest>> RequestsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        return await db.AssetRequests.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId).OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync();
    }

    // ------------------------------------------------------------ bàn giao, sử dụng

    public async Task<AssetAssignment> AssignAsync(SessionContext ctx, Guid assetId, string department, string? custodian, string? location, DateOnly date, string? note)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(department)) throw new DomainException(ErrorCodes.ValidationFailed, "Bộ phận sử dụng bắt buộc.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await db.LongTermAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Thẻ tài sản không tồn tại.");
            if (a.Status != AssetStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Chỉ bàn giao tài sản đang sử dụng; {a.Code} đang ở trạng thái {a.Status}.");
            var open = await db.AssetAssignments.FirstOrDefaultAsync(x => x.AssetId == assetId && x.EndDate == null);
            if (open is not null)
            {
                if (date < open.StartDate) throw new DomainException(ErrorCodes.ValidationFailed, $"Ngày bàn giao không được trước ngày {open.StartDate:dd/MM/yyyy} của lần bàn giao trước.");
                open.EndDate = date;
            }
            var n = new AssetAssignment
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, AssetId = assetId, Department = department.Trim(), Custodian = NullIfBlank(custodian), Location = NullIfBlank(location),
                StartDate = date, Note = NullIfBlank(note), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.AssetAssignments.Add(n);
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset.assign", "asset", assetId.ToString(), new { a.Code, n.Department, n.Custodian, n.Location, date });
            await db.SaveChangesAsync();
            return n;
        });
    }

    public async Task<IReadOnlyList<AssetAssignment>> AssignmentHistoryAsync(SessionContext ctx, Guid assetId)
    {
        ctx.Require(Permissions.ReportView);
        return await db.AssetAssignments.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && x.AssetId == assetId).OrderByDescending(x => x.StartDate).ThenByDescending(x => x.CreatedAt).ToListAsync();
    }

    /// <summary>Mỗi tài sản đang sử dụng và bộ phận / người đang giữ (chưa bàn giao thì để trống).</summary>
    public async Task<IReadOnlyList<AssetCurrentAssignment>> CurrentAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var assets = await db.LongTermAssets.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.Status == AssetStatus.Active).OrderBy(a => a.Code).ToListAsync();
        var open = (await db.AssetAssignments.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && x.EndDate == null).ToListAsync()).ToDictionary(x => x.AssetId);
        return assets.Select(a => open.TryGetValue(a.Id, out var o)
            ? new AssetCurrentAssignment(a.Id, a.Code, a.Name, a.Class, o.Department, o.Custodian, o.Location, o.StartDate)
            : new AssetCurrentAssignment(a.Id, a.Code, a.Name, a.Class, null, null, null, null)).ToList();
    }

    // ------------------------------------------------------------ kiểm kê

    public async Task<AssetCountView> CreateCountAsync(SessionContext ctx, DateOnly date, string? note)
    {
        ctx.Require(Permissions.AssetManage);
        return await db.InTransactionAsync(async () =>
        {
            // Tài sản có hiệu lực tại ngày kiểm kê: đã ghi tăng trước ngày đó và chưa ghi giảm.
            var assets = await db.LongTermAssets.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.ActivatedAt != null
                && a.InServiceDate <= date && (a.Status == AssetStatus.Active || (a.Status == AssetStatus.Disposed && a.DisposalDate > date))).OrderBy(a => a.Code).ToListAsync();
            if (assets.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không có tài sản nào để kiểm kê tại ngày này.");
            var assigns = await db.AssetAssignments.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && x.StartDate <= date && (x.EndDate == null || x.EndDate > date)).ToListAsync();
            var byAsset = assigns.GroupBy(x => x.AssetId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartDate).First().Department);
            var c = new AssetCount { Id = Ids.New(), CompanyId = ctx.CompanyId, CountDate = date, Note = NullIfBlank(note), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Number = await NextNumberAsync(ctx.CompanyId, date, "KKTS") };
            db.AssetCounts.Add(c);
            foreach (var a in assets)
                db.AssetCountLines.Add(new AssetCountLine { Id = Ids.New(), CountId = c.Id, CompanyId = ctx.CompanyId, AssetId = a.Id, BookDepartment = byAsset.GetValueOrDefault(a.Id) });
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_count.create", "asset_count", c.Id.ToString(), new { c.Number, date, assets = assets.Count }, c.RowVersion);
            await db.SaveChangesAsync();
            return await ViewCoreAsync(ctx, c.Id);
        });
    }

    public async Task<AssetCountView> RecordAsync(SessionContext ctx, Guid countId, IReadOnlyList<AssetCountLineInput> lines)
    {
        ctx.Require(Permissions.AssetManage);
        return await db.InTransactionAsync(async () =>
        {
            var c = await db.AssetCounts.FirstOrDefaultAsync(x => x.Id == countId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu kiểm kê không tồn tại.");
            if (c.Status != AssetCountStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {c.Number} đã chốt hoặc hủy, không sửa kết quả.");
            var rows = await db.AssetCountLines.Where(l => l.CountId == countId).ToDictionaryAsync(l => l.AssetId);
            foreach (var l in lines)
            {
                if (!rows.TryGetValue(l.AssetId, out var row)) throw new DomainException(ErrorCodes.NotFound, "Tài sản không thuộc phiếu kiểm kê.");
                if (l.Result is AssetCountResult.Missing or AssetCountResult.Damaged && string.IsNullOrWhiteSpace(l.Note))
                    throw new DomainException(ErrorCodes.ReasonRequired, "Tài sản mất hoặc hỏng phải ghi chú nguyên nhân / tình trạng.");
                row.Result = l.Result; row.ActualDepartment = NullIfBlank(l.ActualDepartment); row.Note = NullIfBlank(l.Note);
            }
            c.RowVersion++;
            await db.SaveChangesAsync();
            return await ViewCoreAsync(ctx, c.Id);
        });
    }

    /// <summary>Chốt phiếu: mọi tài sản đã có kết quả. Người chốt khác người lập khi công ty bắt buộc tách người. Chênh lệch xử lý tiếp bằng ghi giảm / điều tra, không tự ghi sổ.</summary>
    public async Task<AssetCountView> CompleteAsync(SessionContext ctx, Guid countId)
    {
        ctx.Require(Permissions.VoucherApprove);
        return await db.InTransactionAsync(async () =>
        {
            var c = await db.AssetCounts.FirstOrDefaultAsync(x => x.Id == countId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu kiểm kê không tồn tại.");
            if (c.Status != AssetCountStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {c.Number} đã chốt hoặc hủy.");
            var company = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && c.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người chốt kiểm kê không được trùng người lập phiếu.");
            var left = await db.AssetCountLines.CountAsync(l => l.CountId == countId && l.Result == AssetCountResult.NotCounted);
            if (left > 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Còn {left} tài sản chưa có kết quả kiểm kê.");
            c.Status = AssetCountStatus.Completed; c.CompletedBy = ctx.UserId; c.CompletedAt = clock.UtcNow; c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_count.complete", "asset_count", c.Id.ToString(), new { c.Number }, c.RowVersion);
            await db.SaveChangesAsync();
            return await ViewCoreAsync(ctx, c.Id);
        });
    }

    public async Task CancelCountAsync(SessionContext ctx, Guid countId, string reason)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy phiếu kiểm kê phải có lý do.");
        await db.InTransactionAsync(async () =>
        {
            var c = await db.AssetCounts.FirstOrDefaultAsync(x => x.Id == countId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu kiểm kê không tồn tại.");
            if (c.Status != AssetCountStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {c.Number} đã chốt hoặc hủy.");
            c.Status = AssetCountStatus.Cancelled; c.Note = string.IsNullOrWhiteSpace(c.Note) ? reason.Trim() : $"{c.Note} — hủy: {reason.Trim()}"; c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_count.cancel", "asset_count", c.Id.ToString(), new { c.Number, reason }, c.RowVersion);
            await db.SaveChangesAsync();
            return 0;
        });
    }

    public async Task<IReadOnlyList<AssetCountView>> CountsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var ids = await db.AssetCounts.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId).OrderByDescending(c => c.CountDate).ThenByDescending(c => c.Number).Take(20).Select(c => c.Id).ToListAsync();
        var list = new List<AssetCountView>();
        foreach (var id in ids) list.Add(await ViewCoreAsync(ctx, id));
        return list;
    }

    public async Task<AssetCountView> CountAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        return await ViewCoreAsync(ctx, id);
    }

    // ------------------------------------------------------------ internals

    private async Task<AssetCountView> ViewCoreAsync(SessionContext ctx, Guid id)
    {
        var c = await db.AssetCounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu kiểm kê không tồn tại.");
        var lines = await db.AssetCountLines.AsNoTracking().Where(l => l.CountId == id).ToListAsync();
        var ids = lines.Select(l => l.AssetId).ToList();
        var assets = await db.LongTermAssets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var accumulated = (await db.DepreciationEntries.AsNoTracking().Where(e => ids.Contains(e.AssetId) && e.VoidedAt == null && e.PeriodEnd <= c.CountDate).ToListAsync())
            .GroupBy(e => e.AssetId).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var views = lines.Select(l =>
        {
            var a = assets[l.AssetId];
            return new AssetCountLineView(a.Id, a.Code, a.Name, a.Class, l.BookDepartment, l.Result, l.ActualDepartment, l.Note, a.OriginalCost - a.OpeningAccumulated - accumulated.GetValueOrDefault(a.Id));
        }).OrderBy(v => v.Code, StringComparer.Ordinal).ToList();
        bool Moved(AssetCountLineView v) => v.Result == AssetCountResult.Found && v.ActualDepartment != null && !string.Equals(v.ActualDepartment, v.BookDepartment, StringComparison.OrdinalIgnoreCase);
        return new AssetCountView(c.Id, c.Number, c.CountDate, c.Note, c.Status, views.Count, views.Count(v => v.Result != AssetCountResult.NotCounted),
            views.Count(v => v.Result == AssetCountResult.Missing), views.Count(v => v.Result == AssetCountResult.Damaged), views.Count(Moved),
            views.Where(v => v.Result == AssetCountResult.Missing).Sum(v => v.NetBookValue), views);
    }

    private async Task<AssetRequest> LoadRequestAsync(SessionContext ctx, Guid id)
        => await db.AssetRequests.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Đề nghị mua sắm không tồn tại.");

    private async Task<string> NextNumberAsync(Guid companyId, DateOnly date, string prefix)
    {
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
                 ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date.Vn()}.");
        var key = "AST-" + prefix;
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++;
        seq.RowVersion++;
        return $"{prefix}{fy.Code}-{seq.LastNumber:D6}";
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
