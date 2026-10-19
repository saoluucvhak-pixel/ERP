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

public sealed record AssetRepairInput(Guid AssetId, AssetRepairKind Kind, DateOnly ReportedDate, string Description, string? Vendor, decimal EstimatedCost);

public sealed record AssetRepairView(Guid Id, string Number, Guid AssetId, string AssetCode, string AssetName, AssetRepairKind Kind, AssetRepairStatus Status, DateOnly ReportedDate,
    string Description, string? Vendor, decimal EstimatedCost, decimal? ActualCost, DateOnly? CompletedDate, Guid? VoucherId, string? VoucherNumber, string? Note);

/// <summary>
/// Quản lý bảo trì, sửa chữa tài sản: lập phiếu khi phát sinh sự cố hoặc đến kỳ bảo trì → hoàn thành (chi phí thực tế, đơn vị sửa, chứng từ chi phí đã ghi sổ).
/// Không ghi Nhật ký (chi phí ghi sổ bằng hóa đơn mua / phiếu chi thông thường); không đổi nguyên giá và lịch khấu hao của thẻ.
/// </summary>
public sealed class AssetRepairService(HakDbContext db, AuditService audit, IClock clock)
{
    public async Task<AssetRepair> CreateAsync(SessionContext ctx, AssetRepairInput input)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(input.Description)) throw new DomainException(ErrorCodes.ValidationFailed, "Nội dung sự cố / công việc bảo trì bắt buộc.");
        if (input.EstimatedCost < 0 || !MoneyMath.HasAtMostDecimals(input.EstimatedCost, MoneyMath.StorageScale))
            throw new DomainException(ErrorCodes.ValidationFailed, "Chi phí dự kiến không hợp lệ.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await db.LongTermAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.AssetId && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Thẻ tài sản không tồn tại.");
            if (a.Status != AssetStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Chỉ lập phiếu sửa chữa cho tài sản đang sử dụng; {a.Code} đang ở trạng thái {a.Status}.");
            var r = new AssetRepair
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, AssetId = a.Id, Kind = input.Kind, ReportedDate = input.ReportedDate, Description = input.Description.Trim(),
                Vendor = Blank(input.Vendor), EstimatedCost = input.EstimatedCost, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Number = await NextNumberAsync(ctx.CompanyId, input.ReportedDate),
            };
            db.AssetRepairs.Add(r);
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_repair.create", "asset", a.Id.ToString(), new { r.Number, a.Code, input.Kind, r.EstimatedCost }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<AssetRepair> CompleteAsync(SessionContext ctx, Guid id, DateOnly completedDate, decimal actualCost, string? vendor, Guid? voucherId, string? note)
    {
        ctx.Require(Permissions.AssetManage);
        if (actualCost < 0 || !MoneyMath.HasAtMostDecimals(actualCost, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.ValidationFailed, "Chi phí thực tế không hợp lệ.");
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadAsync(ctx, id);
            if (r.Status != AssetRepairStatus.Open) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {r.Number} đã hoàn thành hoặc hủy.");
            if (completedDate < r.ReportedDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày hoàn thành không được trước ngày lập phiếu.");
            if (voucherId is { } vid)
            {
                var v = await db.Vouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == vid && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Chứng từ chi phí không tồn tại.");
                if (v.Status != VoucherStatus.Posted) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Chứng từ {v.Number} chưa ghi sổ.");
            }
            r.Status = AssetRepairStatus.Completed; r.CompletedDate = completedDate; r.ActualCost = actualCost; r.CompletedBy = ctx.UserId;
            if (!string.IsNullOrWhiteSpace(vendor)) r.Vendor = vendor.Trim();
            r.VoucherId = voucherId; r.Note = Blank(note); r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_repair.complete", "asset", r.AssetId.ToString(), new { r.Number, actualCost, voucherId }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<AssetRepair> CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.AssetManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy phiếu sửa chữa phải có lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var r = await LoadAsync(ctx, id);
            if (r.Status != AssetRepairStatus.Open) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {r.Number} đã hoàn thành hoặc hủy.");
            r.Status = AssetRepairStatus.Cancelled; r.Note = reason.Trim(); r.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "asset_repair.cancel", "asset", r.AssetId.ToString(), new { r.Number, reason }, r.RowVersion);
            await db.SaveChangesAsync();
            return r;
        });
    }

    public async Task<IReadOnlyList<AssetRepairView>> ListAsync(SessionContext ctx, Guid? assetId = null)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.AssetRepairs.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId);
        if (assetId != null) q = q.Where(r => r.AssetId == assetId);
        var rows = await q.OrderByDescending(r => r.ReportedDate).ThenByDescending(r => r.Number).Take(300).ToListAsync();
        var aids = rows.Select(r => r.AssetId).Distinct().ToList();
        var assets = await db.LongTermAssets.AsNoTracking().Where(a => aids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var vids = rows.Where(r => r.VoucherId != null).Select(r => r.VoucherId!.Value).ToList();
        var vouchers = await db.Vouchers.AsNoTracking().Where(v => vids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Number);
        return rows.Select(r => new AssetRepairView(r.Id, r.Number, r.AssetId, assets[r.AssetId].Code, assets[r.AssetId].Name, r.Kind, r.Status, r.ReportedDate, r.Description, r.Vendor,
            r.EstimatedCost, r.ActualCost, r.CompletedDate, r.VoucherId, r.VoucherId is { } v ? vouchers.GetValueOrDefault(v) : null, r.Note)).ToList();
    }

    private async Task<AssetRepair> LoadAsync(SessionContext ctx, Guid id)
        => await db.AssetRepairs.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phiếu sửa chữa không tồn tại.");

    private async Task<string> NextNumberAsync(Guid companyId, DateOnly date)
    {
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
                 ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date}.");
        const string key = "AST-SCTS";
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++; seq.RowVersion++;
        return $"SCTS{fy.Code}-{seq.LastNumber:D6}";
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
