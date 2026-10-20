using Hak.Data;
using Hak.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

public sealed record PartnerTerms(decimal? CreditLimit, int? PaymentTermsDays, string? LimitSource, string? DaysSource);

/// <summary>
/// Mặc định theo nhóm (chị giao em tham khảo các ERP khác — SAP nhóm hàng, Odoo danh mục sản phẩm, ERPNext Item Group, Dynamics item group — đều: giá trị khai trên chính mục thắng,
/// nếu không thì lấy từ MỘT nhóm chính và kế thừa lên nhóm cha). Vì ở đây một mục thuộc nhiều nhóm, thứ tự cho từng chỉ tiêu là:
/// (1) giá trị khai trên chính mục;
/// (2) NHÓM CHÍNH của mục (do người dùng chọn; mục chỉ ở một nhóm thì nhóm đó là nhóm chính) — nhóm chính chưa khai thì kế thừa nhóm cha gần nhất có khai;
/// (3) chưa chọn nhóm chính: xét mọi nhóm đang dùng (cũng kế thừa nhóm cha), nhóm sâu nhất thắng, cùng độ sâu thì lấy giá trị CHẶT nhất (hạn mức thấp nhất, hạn thanh toán ngắn nhất, thuế suất cao nhất).
/// Nhóm ngừng dùng bị bỏ qua; nguồn được trả về để hiển thị.
/// </summary>
public static class GroupDefaults
{
    public static async Task<PartnerTerms> PartnerTermsAsync(HakDbContext db, Guid companyId, BusinessPartner partner)
    {
        var limit = await ResolveAsync<decimal>(db, companyId, GroupDomain.Partner, partner.Id, partner.CreditLimit, g => g.DefaultCreditLimit, v => v.Min());
        var days = await ResolveAsync<int>(db, companyId, GroupDomain.Partner, partner.Id, partner.PaymentTermsDays, g => g.DefaultPaymentTermsDays, v => v.Min());
        return new PartnerTerms(limit.Value, days.Value, limit.Source, days.Source);
    }

    /// <summary>Thuế suất GTGT mặc định của vật tư: vật tư khai &gt; nhóm vật tư.</summary>
    public static async Task<(decimal? Value, string? Source)> ItemVatRateAsync(HakDbContext db, Guid companyId, Hak.Domain.Inventory.Item item)
        => await ResolveAsync<decimal>(db, companyId, GroupDomain.Item, item.Id, item.DefaultVatRate, g => g.DefaultVatRate, v => v.Max());

    public static async Task<(T? Value, string? Source)> ResolveAsync<T>(HakDbContext db, Guid companyId, GroupDomain domain, Guid entityId, T? own, Func<CatalogGroup, T?> get, Func<IEnumerable<T>, T> strictest) where T : struct
    {
        if (own is not null) return (own, "đối tượng");
        var members = await db.CatalogGroupMembers.AsNoTracking().Where(m => m.EntityId == entityId && m.CompanyId == companyId).Select(m => new { m.GroupId, m.IsPrimary }).ToListAsync();
        if (members.Count == 0) return (null, null);
        var all = await db.CatalogGroups.AsNoTracking().Where(g => g.CompanyId == companyId && g.Domain == domain).ToDictionaryAsync(g => g.Id);
        int Depth(CatalogGroup g) { var d = 0; for (var c = g.ParentId; c is not null && all.TryGetValue(c.Value, out var p); c = p.ParentId) d++; return d; }
        // Giá trị hiệu lực của một nhóm: của chính nó, không có thì của nhóm cha gần nhất có khai (bỏ qua nhóm ngừng dùng).
        (T? v, CatalogGroup? from) Inherited(CatalogGroup g)
        {
            for (CatalogGroup? c = g; c is not null; c = c.ParentId is { } pid && all.TryGetValue(pid, out var p) ? p : null)
                if (c.IsActive && get(c) is { } x) return (x, c);
            return (null, null);
        }
        var active = members.Where(m => all.TryGetValue(m.GroupId, out var g) && g.IsActive).ToList();
        var primary = active.FirstOrDefault(m => m.IsPrimary) ?? (active.Count == 1 ? active[0] : null);
        if (primary is not null)
        {
            var r = Inherited(all[primary.GroupId]);
            return r.v is { } pv ? (pv, "nhóm " + r.from!.Code) : (null, null);
        }
        var found = active.Select(m => all[m.GroupId]).Select(g => (g, r: Inherited(g))).Where(x => x.r.v is not null).ToList();
        if (found.Count == 0) return (null, null);
        var deepest = found.Max(x => Depth(x.g));
        var top = found.Where(x => Depth(x.g) == deepest).ToList();
        return (strictest(top.Select(x => x.r.v!.Value)), "nhóm " + string.Join(" / ", top.Select(x => x.r.from!.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal)));
    }
}
