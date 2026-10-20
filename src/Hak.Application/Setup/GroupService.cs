using System.Text.RegularExpressions;
using Hak.Application.Assets;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

public sealed record GroupInput(GroupDomain Domain, string Code, string Name, Guid? ParentId, string? Note = null);
public sealed record GroupRow(Guid Id, GroupDomain Domain, string Code, string Name, Guid? ParentId, int Level, bool IsActive, string? Note, int DirectMembers, int AllMembers, int Children);
public sealed record GroupMemberRow(Guid EntityId, string Code, string Name, IReadOnlyList<Guid> OtherGroupIds);
public sealed record EntityGroupLink(Guid EntityId, Guid GroupId);
public sealed record GroupSummaryColumn(string Key, string Label);
public sealed record GroupSummaryRow(Guid? GroupId, string Code, string Name, int Level, int Count, IReadOnlyDictionary<string, decimal> Values);
public sealed record GroupSummary(IReadOnlyList<GroupSummaryColumn> Columns, IReadOnlyList<GroupSummaryRow> Rows, IReadOnlyDictionary<string, decimal> UngroupedValues, int UngroupedCount);

/// <summary>
/// Nhóm danh mục nhiều cấp, mỗi mục thuộc nhiều nhóm (chị chốt 20/10/2026). Cây nhóm riêng cho từng loại danh mục (đối tượng, vật tư, tài sản / CCDC, kho); nhóm cha tự gồm các nhóm con khi xem thành viên,
/// lọc và báo cáo. Một mục ở nhiều nhóm thì được tính vào từng nhóm — dòng tổng của báo cáo theo nhóm vì vậy không cộng các nhóm (có dòng "chưa thuộc nhóm nào" và tổng toàn bộ riêng).
/// Nhóm chỉ để phân tích, không đổi số liệu đã ghi sổ; mọi thay đổi có nhật ký kiểm toán.
/// </summary>
public sealed class GroupService(HakDbContext db, AuditService audit, IClock clock, AssetService assets, StockService stock)
{
    private static readonly Regex CodeRule = new("^[A-Z0-9][A-Z0-9\\-]{0,19}$", RegexOptions.Compiled);

    // ------------------------------------------------------------------ cây nhóm

    public async Task<CatalogGroup> CreateAsync(SessionContext ctx, GroupInput input)
    {
        ctx.Require(Permissions.CatalogManage);
        var code = (input.Code ?? "").Trim().ToUpperInvariant();
        if (!CodeRule.IsMatch(code)) throw new DomainException(ErrorCodes.ValidationFailed, "Mã nhóm gồm chữ hoa, số và dấu -, tối đa 20 ký tự, bắt đầu bằng chữ hoặc số.");
        if (string.IsNullOrWhiteSpace(input.Name)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên nhóm bắt buộc.");
        return await db.InTransactionAsync(async () =>
        {
            if (await db.CatalogGroups.AnyAsync(g => g.CompanyId == ctx.CompanyId && g.Domain == input.Domain && g.Code == code))
                throw new DomainException(ErrorCodes.DuplicateCode, $"Mã nhóm {code} đã tồn tại.");
            if (input.ParentId is { } pid) await ParentAsync(ctx, input.Domain, pid, null);
            var g = new CatalogGroup { Id = Ids.New(), CompanyId = ctx.CompanyId, Domain = input.Domain, Code = code, Name = input.Name.Trim(), ParentId = input.ParentId, Note = input.Note, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.CatalogGroups.Add(g);
            await audit.AppendAsync(ctx.CompanyId, ctx, "group.create", "catalog_group", g.Id.ToString(), new { input.Domain, code, g.Name, input.ParentId });
            await db.SaveChangesAsync();
            return g;
        });
    }

    /// <summary>Đổi tên, nhóm cha (không tạo vòng), ghi chú, ngừng dùng. Mã không đổi sau khi tạo.</summary>
    public async Task UpdateAsync(SessionContext ctx, Guid id, string name, Guid? parentId, string? note, bool isActive)
    {
        ctx.Require(Permissions.CatalogManage);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên nhóm bắt buộc.");
        await db.InTransactionAsync(async () =>
        {
            var g = await db.CatalogGroups.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Nhóm không tồn tại.");
            if (parentId is { } pid) await ParentAsync(ctx, g.Domain, pid, g.Id);
            g.Name = name.Trim(); g.ParentId = parentId; g.Note = note; g.IsActive = isActive;
            await audit.AppendAsync(ctx.CompanyId, ctx, "group.update", "catalog_group", g.Id.ToString(), new { g.Code, g.Name, parentId, isActive });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Chỉ xóa được nhóm không có nhóm con và không có thành viên (còn thì ngừng dùng).</summary>
    public async Task DeleteAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            var g = await db.CatalogGroups.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Nhóm không tồn tại.");
            if (await db.CatalogGroups.AnyAsync(x => x.ParentId == id)) throw new DomainException(ErrorCodes.ValidationFailed, "Nhóm còn nhóm con; xóa hoặc chuyển nhóm con trước.");
            if (await db.CatalogGroupMembers.AnyAsync(m => m.GroupId == id)) throw new DomainException(ErrorCodes.ValidationFailed, "Nhóm còn thành viên; bỏ thành viên hoặc chuyển sang ngừng dùng.");
            db.CatalogGroups.Remove(g);
            await audit.AppendAsync(ctx.CompanyId, ctx, "group.delete", "catalog_group", id.ToString(), new { g.Code, g.Name });
            await db.SaveChangesAsync();
        });
    }

    private async Task ParentAsync(SessionContext ctx, GroupDomain domain, Guid parentId, Guid? self)
    {
        var all = await db.CatalogGroups.AsNoTracking().Where(g => g.CompanyId == ctx.CompanyId && g.Domain == domain).ToDictionaryAsync(g => g.Id);
        if (!all.TryGetValue(parentId, out var parent)) throw new DomainException(ErrorCodes.NotFound, "Nhóm cha không tồn tại hoặc thuộc loại danh mục khác.");
        if (!parent.IsActive && self is null) throw new DomainException(ErrorCodes.ValidationFailed, "Nhóm cha đã ngừng dùng.");
        for (Guid? cur = parentId; cur is not null; cur = all.TryGetValue(cur.Value, out var c) ? c.ParentId : null)
            if (cur == self) throw new DomainException(ErrorCodes.ValidationFailed, "Không đặt nhóm làm con của chính nó hoặc của nhóm con của nó (tạo vòng).");
    }

    public async Task<IReadOnlyList<GroupRow>> TreeAsync(SessionContext ctx, GroupDomain domain)
    {
        ctx.Require(Permissions.VoucherView);
        var groups = await db.CatalogGroups.AsNoTracking().Where(g => g.CompanyId == ctx.CompanyId && g.Domain == domain).ToListAsync();
        var members = await (from m in db.CatalogGroupMembers join g in db.CatalogGroups on m.GroupId equals g.Id where g.CompanyId == ctx.CompanyId && g.Domain == domain select new { m.GroupId, m.EntityId }).AsNoTracking().ToListAsync();
        var kids = groups.GroupBy(g => g.ParentId ?? Guid.Empty).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Code, StringComparer.Ordinal).ToList());
        var rows = new List<GroupRow>();
        void Walk(Guid parent, int level)
        {
            foreach (var g in kids.GetValueOrDefault(parent) ?? [])
            {
                var ids = DescendantIds(g.Id, kids);
                rows.Add(new GroupRow(g.Id, g.Domain, g.Code, g.Name, g.ParentId, level, g.IsActive, g.Note, members.Count(m => m.GroupId == g.Id),
                    members.Where(m => ids.Contains(m.GroupId)).Select(m => m.EntityId).Distinct().Count(), kids.GetValueOrDefault(g.Id)?.Count ?? 0));
                Walk(g.Id, level + 1);
            }
        }
        Walk(Guid.Empty, 0);
        return rows;
    }

    private static HashSet<Guid> DescendantIds(Guid id, Dictionary<Guid, List<CatalogGroup>> kids)
    {
        var set = new HashSet<Guid> { id };
        var stack = new Stack<Guid>([id]);
        while (stack.Count > 0) foreach (var c in kids.GetValueOrDefault(stack.Pop()) ?? []) if (set.Add(c.Id)) stack.Push(c.Id);
        return set;
    }

    // ------------------------------------------------------------------ thành viên

    /// <summary>Đặt toàn bộ nhóm của một mục (thay tập nhóm hiện có của loại danh mục đó). Mục được ở nhiều nhóm; chỉ chọn nhóm đang dùng cho nhóm mới.</summary>
    public async Task SetGroupsOfAsync(SessionContext ctx, GroupDomain domain, Guid entityId, IReadOnlyList<Guid> groupIds)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            await EnsureEntityAsync(ctx, domain, entityId);
            var wanted = groupIds.Distinct().ToHashSet();
            var groups = await db.CatalogGroups.AsNoTracking().Where(g => g.CompanyId == ctx.CompanyId && g.Domain == domain).ToDictionaryAsync(g => g.Id);
            var current = await (from m in db.CatalogGroupMembers join g in db.CatalogGroups on m.GroupId equals g.Id where m.CompanyId == ctx.CompanyId && g.Domain == domain && m.EntityId == entityId select m).ToListAsync();
            foreach (var gid in wanted)
                if (!groups.TryGetValue(gid, out var g)) throw new DomainException(ErrorCodes.NotFound, "Có nhóm không tồn tại hoặc thuộc loại danh mục khác.");
                else if (!g.IsActive && !current.Any(m => m.GroupId == gid)) throw new DomainException(ErrorCodes.ValidationFailed, $"Nhóm {g.Code} đã ngừng dùng.");
            db.CatalogGroupMembers.RemoveRange(current.Where(m => !wanted.Contains(m.GroupId)));
            foreach (var gid in wanted.Where(g => !current.Any(m => m.GroupId == g)))
                db.CatalogGroupMembers.Add(new CatalogGroupMember { Id = Ids.New(), CompanyId = ctx.CompanyId, GroupId = gid, EntityId = entityId });
            await audit.AppendAsync(ctx.CompanyId, ctx, "group.assign", "catalog_group", entityId.ToString(), new { domain, groups = wanted.Select(g => groups[g].Code).OrderBy(c => c).ToArray() });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Thêm nhiều mục vào một nhóm (bỏ qua mục đã có). Trả số mục mới thêm.</summary>
    public async Task<int> AddMembersAsync(SessionContext ctx, Guid groupId, IReadOnlyList<Guid> entityIds)
    {
        ctx.Require(Permissions.CatalogManage);
        return await db.InTransactionAsync(async () =>
        {
            var g = await db.CatalogGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Nhóm không tồn tại.");
            if (!g.IsActive) throw new DomainException(ErrorCodes.ValidationFailed, $"Nhóm {g.Code} đã ngừng dùng.");
            var have = (await db.CatalogGroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).Select(m => m.EntityId).ToListAsync()).ToHashSet();
            var added = 0;
            foreach (var id in entityIds.Distinct().Where(i => !have.Contains(i)))
            {
                await EnsureEntityAsync(ctx, g.Domain, id);
                db.CatalogGroupMembers.Add(new CatalogGroupMember { Id = Ids.New(), CompanyId = ctx.CompanyId, GroupId = groupId, EntityId = id }); added++;
            }
            if (added > 0) await audit.AppendAsync(ctx.CompanyId, ctx, "group.add_members", "catalog_group", groupId.ToString(), new { g.Code, added });
            await db.SaveChangesAsync();
            return added;
        });
    }

    public async Task RemoveMembersAsync(SessionContext ctx, Guid groupId, IReadOnlyList<Guid> entityIds)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            var g = await db.CatalogGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Nhóm không tồn tại.");
            var rows = await db.CatalogGroupMembers.Where(m => m.GroupId == groupId && entityIds.Contains(m.EntityId)).ToListAsync();
            db.CatalogGroupMembers.RemoveRange(rows);
            if (rows.Count > 0) await audit.AppendAsync(ctx.CompanyId, ctx, "group.remove_members", "catalog_group", groupId.ToString(), new { g.Code, removed = rows.Count });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Quan hệ mục – nhóm của một loại danh mục (cho cột "Nhóm" và bộ lọc theo nhóm ở các màn danh mục).</summary>
    public async Task<IReadOnlyList<EntityGroupLink>> LinksAsync(SessionContext ctx, GroupDomain domain)
    {
        ctx.Require(Permissions.VoucherView);
        return await (from m in db.CatalogGroupMembers join g in db.CatalogGroups on m.GroupId equals g.Id where g.CompanyId == ctx.CompanyId && g.Domain == domain
                      select new EntityGroupLink(m.EntityId, m.GroupId)).AsNoTracking().ToListAsync();
    }

    /// <summary>Thành viên của nhóm (gồm cả nhóm con khi <paramref name="includeDescendants"/>), kèm các nhóm khác mà mục đó thuộc.</summary>
    public async Task<IReadOnlyList<GroupMemberRow>> MembersAsync(SessionContext ctx, Guid groupId, bool includeDescendants)
    {
        ctx.Require(Permissions.VoucherView);
        var g = await db.CatalogGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Nhóm không tồn tại.");
        var all = await db.CatalogGroups.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && x.Domain == g.Domain).ToListAsync();
        var kids = all.GroupBy(x => x.ParentId ?? Guid.Empty).ToDictionary(x => x.Key, x => x.ToList());
        var ids = includeDescendants ? DescendantIds(groupId, kids) : [groupId];
        var links = (await LinksAsync(ctx, g.Domain)).GroupBy(l => l.EntityId).ToDictionary(x => x.Key, x => x.Select(l => l.GroupId).ToList());
        var members = links.Where(kv => kv.Value.Any(ids.Contains)).Select(kv => kv.Key).ToList();
        var names = await NamesAsync(ctx, g.Domain, members);
        return members.Select(e => new GroupMemberRow(e, names[e].Code, names[e].Name, links[e].Where(x => !ids.Contains(x)).ToList())).OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    private async Task<Dictionary<Guid, (string Code, string Name)>> NamesAsync(SessionContext ctx, GroupDomain domain, IReadOnlyList<Guid> ids)
    {
        var rows = domain switch
        {
            GroupDomain.Partner => await db.BusinessPartners.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && ids.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(),
            GroupDomain.Item => await db.Items.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && ids.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(),
            GroupDomain.Asset => await db.LongTermAssets.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && ids.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(),
            _ => await db.Warehouses.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && ids.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(),
        };
        return rows.ToDictionary(r => r.Id, r => (r.Code, r.Name));
    }

    private async Task EnsureEntityAsync(SessionContext ctx, GroupDomain domain, Guid id)
    {
        var ok = domain switch
        {
            GroupDomain.Partner => await db.BusinessPartners.AnyAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId),
            GroupDomain.Item => await db.Items.AnyAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId),
            GroupDomain.Asset => await db.LongTermAssets.AnyAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId),
            _ => await db.Warehouses.AnyAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId),
        };
        if (!ok) throw new DomainException(ErrorCodes.NotFound, "Mục cần xếp nhóm không tồn tại hoặc thuộc công ty khác.");
    }

    // ------------------------------------------------------------------ báo cáo theo nhóm

    /// <summary>
    /// Báo cáo theo nhóm (nhóm cha gồm nhóm con): đối tượng — phải thu / phải trả còn mở; vật tư — giá trị tồn; tài sản — nguyên giá, hao mòn, giá trị còn lại;
    /// kho — giá trị tồn. Mục ở nhiều nhóm được tính vào từng nhóm nên các dòng nhóm không cộng được; có dòng "chưa thuộc nhóm nào".
    /// </summary>
    public async Task<GroupSummary> SummaryAsync(SessionContext ctx, GroupDomain domain, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var (columns, perEntity) = await MetricsAsync(ctx, domain, asOf);
        var groups = await db.CatalogGroups.AsNoTracking().Where(g => g.CompanyId == ctx.CompanyId && g.Domain == domain).ToListAsync();
        var kids = groups.GroupBy(g => g.ParentId ?? Guid.Empty).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Code, StringComparer.Ordinal).ToList());
        var links = (await LinksAsync(ctx, domain)).GroupBy(l => l.GroupId).ToDictionary(g => g.Key, g => g.Select(l => l.EntityId).ToHashSet());
        var rows = new List<GroupSummaryRow>();
        void Walk(Guid parent, int level)
        {
            foreach (var g in kids.GetValueOrDefault(parent) ?? [])
            {
                var ids = DescendantIds(g.Id, kids);
                var entities = ids.SelectMany(i => links.GetValueOrDefault(i) ?? []).ToHashSet();
                rows.Add(new GroupSummaryRow(g.Id, g.Code, g.Name, level, entities.Count, Sum(columns, entities.Select(e => perEntity.GetValueOrDefault(e)))));
                Walk(g.Id, level + 1);
            }
        }
        Walk(Guid.Empty, 0);
        var grouped = links.Values.SelectMany(x => x).ToHashSet();
        var ungrouped = perEntity.Where(kv => !grouped.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        return new GroupSummary(columns, rows, Sum(columns, ungrouped), ungrouped.Count);
    }

    private static Dictionary<string, decimal> Sum(IReadOnlyList<GroupSummaryColumn> cols, IEnumerable<Dictionary<string, decimal>?> items)
    {
        var list = items.ToList();
        return cols.ToDictionary(c => c.Key, c => list.Sum(d => d is not null && d.TryGetValue(c.Key, out var v) ? v : 0m));
    }

    private async Task<(IReadOnlyList<GroupSummaryColumn>, Dictionary<Guid, Dictionary<string, decimal>>)> MetricsAsync(SessionContext ctx, GroupDomain domain, DateOnly asOf)
    {
        switch (domain)
        {
            case GroupDomain.Asset:
            {
                var reg = await assets.RegisterAsync(ctx, asOf);
                return ([new("cost", "Nguyên giá"), new("accumulated", "Hao mòn lũy kế"), new("net", "Giá trị còn lại")],
                    reg.Where(r => r.Status == Domain.Assets.AssetStatus.Active).ToDictionary(r => r.Id, r => new Dictionary<string, decimal> { ["cost"] = r.OriginalCost, ["accumulated"] = r.Accumulated, ["net"] = r.NetBookValue }));
            }
            case GroupDomain.Item or GroupDomain.Warehouse:
            {
                var rows = await stock.SummaryAsync(ctx, new DateOnly(2000, 1, 1), asOf);
                var key = domain == GroupDomain.Item ? (Func<InventorySummaryRow, Guid>)(r => r.ItemId) : r => r.WarehouseId;
                var map = rows.GroupBy(key).ToDictionary(g => g.Key, g => new Dictionary<string, decimal> { ["value"] = g.Sum(r => r.ClosingValue) });
                // Mục chưa có tồn vẫn được tính (giá trị 0) để đếm đủ số mục trong nhóm.
                var ids = domain == GroupDomain.Item ? await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).Select(i => i.Id).ToListAsync() : await db.Warehouses.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).Select(i => i.Id).ToListAsync();
                foreach (var id in ids) map.TryAdd(id, new Dictionary<string, decimal> { ["value"] = 0 });
                return ([new("value", "Giá trị tồn kho")], map);
            }
            default:
            {
                var result = new Dictionary<Guid, Dictionary<string, decimal>>();
                foreach (var p in await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).Select(p => p.Id).ToListAsync()) result[p] = new() { ["receivable"] = 0, ["payable"] = 0 };
                var settlement = new SettlementService(db, audit, clock);
                foreach (var (role, key, side) in new[] { ("AR_TRADE", "receivable", Side.Debit), ("AP_TRADE", "payable", Side.Credit) })
                {
                    var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role);
                    if (map is null) continue;
                    foreach (var o in await settlement.OpenItemsAsync(ctx, map.AccountId, null, asOf))
                        if (o.Side == side && result.TryGetValue(o.PartnerId, out var d)) d[key] += o.Open;
                }
                return ([new("receivable", "Phải thu còn mở"), new("payable", "Phải trả còn mở")], result);
            }
        }
    }
}
