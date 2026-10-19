using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Assets;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Assets;

/// <summary>Một dòng trong Nhật ký tài sản. Group: Hình thành, Sử dụng, Sửa chữa, Kiểm kê, Giá trị, Kết thúc.</summary>
public sealed record AssetJournalEvent(DateOnly Date, DateTime At, string Group, string Title, string? Detail, decimal? Amount, string? Reference, string? By, bool Voided = false);

public sealed record AssetJournal(Guid AssetId, string Code, string Name, AssetClass Class, AssetStatus Status, decimal OriginalCost, decimal Accumulated, decimal NetBookValue,
    string? Department, string? Custodian, decimal RepairCost, int RepairCount, IReadOnlyList<AssetJournalEvent> Events);

/// <summary>
/// Nhật ký tài sản: toàn bộ thông tin và biến động của một tài sản theo thời gian — lập thẻ, nguồn nguyên giá, đề nghị mua sắm, ghi tăng, bàn giao, bảo trì / sửa chữa,
/// kiểm kê, khấu hao / phân bổ (kể cả bút toán đã hủy), ghi giảm và hủy ghi giảm. Chỉ đọc, dựng từ dữ liệu gốc, không lưu thêm.
/// </summary>
public sealed class AssetJournalService(HakDbContext db)
{
    public async Task<AssetJournal> GetAsync(SessionContext ctx, Guid assetId)
    {
        ctx.Require(Permissions.ReportView);
        var a = await db.LongTermAssets.AsNoTracking().Include(x => x.CostSources).FirstOrDefaultAsync(x => x.Id == assetId && x.CompanyId == ctx.CompanyId)
                ?? throw new Hak.Domain.Common.DomainException(Hak.Domain.Common.ErrorCodes.NotFound, "Thẻ tài sản không tồn tại.");
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName);
        string? Who(Guid? id) => id is { } i && users.TryGetValue(i, out var n) ? n : null;
        var ev = new List<AssetJournalEvent>();
        DateOnly D(DateTime t) => DateOnly.FromDateTime(t);

        ev.Add(new(D(a.CreatedAt), a.CreatedAt, "Hình thành", "Lập thẻ tài sản", $"{(a.Class == AssetClass.FixedAsset ? "TSCĐ" : "CCDC")}; thời gian sử dụng {a.UsefulLifeMonths} tháng", null, a.Code, Who(a.CreatedBy)));

        var req = await db.AssetRequests.AsNoTracking().FirstOrDefaultAsync(r => r.AssetId == assetId);
        if (req != null)
        {
            ev.Add(new(D(req.CreatedAt), req.CreatedAt, "Hình thành", "Đề nghị mua sắm", req.Reason, req.EstimatedCost, req.Number, Who(req.CreatedBy)));
            if (req.DecidedAt is { } da) ev.Add(new(D(da), da, "Hình thành", req.Status == AssetRequestStatus.Rejected ? "Từ chối đề nghị" : "Duyệt đề nghị mua sắm", req.DecisionNote, null, req.Number, Who(req.DecidedBy)));
        }

        var lineIds = a.CostSources.Select(s => s.JournalLineId).ToList();
        var lines = await (from l in db.JournalLines join e in db.JournalEntries on l.JournalEntryId equals e.Id
                           where lineIds.Contains(l.Id) select new { l.Id, e.VoucherNumber, l.PostingDate }).AsNoTracking().ToListAsync();
        foreach (var s in a.CostSources)
        {
            var l = lines.FirstOrDefault(x => x.Id == s.JournalLineId);
            if (l != null) ev.Add(new(l.PostingDate, a.CreatedAt, "Giá trị", "Nguồn nguyên giá", "Bút toán đã ghi sổ gán cho thẻ", s.Amount, l.VoucherNumber, null));
        }
        if (a.ActivatedAt is { } act)
            ev.Add(new(a.InServiceDate, act, "Hình thành", "Ghi tăng, đưa vào sử dụng", $"Bắt đầu khấu hao / phân bổ {a.ScheduleStartDate:dd/MM/yyyy}", a.OriginalCost, a.Code, Who(a.ActivatedBy)));

        foreach (var s in await db.AssetAssignments.AsNoTracking().Where(x => x.AssetId == assetId).ToListAsync())
            ev.Add(new(s.StartDate, s.CreatedAt, "Sử dụng", "Bàn giao / điều chuyển",
                string.Join("; ", new[] { $"Bộ phận {s.Department}", s.Custodian is null ? null : $"người giữ {s.Custodian}", s.Location is null ? null : $"vị trí {s.Location}", s.Note }.Where(x => x != null)), null, null, Who(s.CreatedBy)));

        var repairs = await db.AssetRepairs.AsNoTracking().Where(r => r.AssetId == assetId).ToListAsync();
        var vids = repairs.Where(r => r.VoucherId != null).Select(r => r.VoucherId!.Value).ToList();
        var vnum = await db.Vouchers.AsNoTracking().Where(v => vids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Number);
        foreach (var r in repairs)
        {
            var kind = r.Kind == AssetRepairKind.Maintenance ? "Bảo trì" : "Sửa chữa";
            ev.Add(new(r.ReportedDate, r.CreatedAt, "Sửa chữa", $"Lập phiếu {kind.ToLowerInvariant()}", r.Description, r.EstimatedCost, r.Number, Who(r.CreatedBy)));
            if (r.Status == AssetRepairStatus.Completed && r.CompletedDate is { } cd)
                ev.Add(new(cd, r.CreatedAt.AddTicks(1), "Sửa chữa", $"Hoàn thành {kind.ToLowerInvariant()}", string.Join("; ", new[] { r.Vendor is null ? null : $"đơn vị: {r.Vendor}", r.Note }.Where(x => x != null)),
                    r.ActualCost, r.VoucherId is { } v && vnum.TryGetValue(v, out var n) ? $"{r.Number} · CT {n}" : r.Number, Who(r.CompletedBy)));
            if (r.Status == AssetRepairStatus.Cancelled) ev.Add(new(r.ReportedDate, r.CreatedAt.AddTicks(1), "Sửa chữa", "Hủy phiếu", r.Note, null, r.Number, null, true));
        }

        var counts = await (from l in db.AssetCountLines join c in db.AssetCounts on l.CountId equals c.Id
                            where l.AssetId == assetId && c.Status == AssetCountStatus.Completed select new { c.Number, c.CountDate, c.CompletedAt, c.CompletedBy, l.Result, l.ActualDepartment, l.Note }).AsNoTracking().ToListAsync();
        foreach (var c in counts)
            ev.Add(new(c.CountDate, c.CompletedAt ?? a.CreatedAt, "Kiểm kê", c.Result switch { AssetCountResult.Found => "Kiểm kê: có, đúng sổ", AssetCountResult.Missing => "Kiểm kê: không thấy", AssetCountResult.Damaged => "Kiểm kê: hỏng", _ => "Kiểm kê" },
                string.Join("; ", new[] { c.ActualDepartment is null ? null : $"thực tế ở {c.ActualDepartment}", c.Note }.Where(x => x != null)), null, c.Number, Who(c.CompletedBy)));

        var dep = await db.DepreciationEntries.AsNoTracking().Where(e => e.AssetId == assetId).ToListAsync();
        foreach (var e in dep)
            ev.Add(new(e.PeriodEnd, e.CreatedAt, "Giá trị", e.IsDisposal ? "Khấu hao phần cuối khi ghi giảm" : (a.Class == AssetClass.FixedAsset ? "Khấu hao kỳ " : "Phân bổ kỳ ") + e.PeriodEnd.ToString("MM/yyyy"),
                e.VoidedAt != null ? "Đã hủy" : null, e.Amount, null, null, e.VoidedAt != null));

        if (a.Status == AssetStatus.Disposed && a.DisposalDate is { } dd)
        {
            var vn = a.DisposalVoucherId is { } dv ? await db.Vouchers.AsNoTracking().Where(v => v.Id == dv).Select(v => v.Number).FirstOrDefaultAsync() : null;
            ev.Add(new(dd, a.CreatedAt, "Kết thúc", "Ghi giảm, thanh lý", null, null, vn, null));
        }
        var logs = await db.AuditLogs.AsNoTracking().Where(l => l.CompanyId == ctx.CompanyId && l.EntityType == "asset" && l.EntityId == assetId.ToString()
            && (l.Action == "asset.dispose_reverse" || l.Action == "asset.cancel")).ToListAsync();
        foreach (var l in logs)
            ev.Add(new(D(l.At), l.At, "Kết thúc", l.Action == "asset.cancel" ? "Hủy thẻ tài sản" : "Hủy ghi giảm", ReasonOf(l.DataJson), null, null, Who(l.UserId)));

        var accumulated = a.OpeningAccumulated + dep.Where(e => e.VoidedAt == null).Sum(e => e.Amount);
        var cur = await db.AssetAssignments.AsNoTracking().Where(x => x.AssetId == assetId && x.EndDate == null).FirstOrDefaultAsync();
        var done = repairs.Where(r => r.Status == AssetRepairStatus.Completed).ToList();
        return new AssetJournal(a.Id, a.Code, a.Name, a.Class, a.Status, a.OriginalCost, accumulated, a.OriginalCost - accumulated, cur?.Department, cur?.Custodian,
            done.Sum(r => r.ActualCost ?? 0), done.Count, ev.OrderBy(e => e.Date).ThenBy(e => e.At).ToList());
    }

    private static string? ReasonOf(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var d = System.Text.Json.JsonDocument.Parse(json); return d.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null; } catch { return null; }
    }
}
