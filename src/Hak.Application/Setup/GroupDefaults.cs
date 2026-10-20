using Hak.Data;
using Hak.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

public sealed record PartnerTerms(decimal? CreditLimit, int? PaymentTermsDays, string? LimitSource, string? DaysSource);

/// <summary>
/// Mặc định theo nhóm cho đối tượng (chị giao em đề xuất quy tắc ưu tiên, 20/10/2026). Với từng chỉ tiêu, theo thứ tự:
/// (1) giá trị khai trên chính đối tượng; (2) nhóm CỤ THỂ NHẤT — nhóm sâu nhất trong cây (cấp con thắng cấp cha) có khai giá trị;
/// (3) nếu nhiều nhóm cùng độ sâu thì lấy giá trị THẬN TRỌNG nhất (hạn mức thấp nhất, hạn thanh toán ngắn nhất) để không nới tín dụng ngoài ý muốn.
/// Mỗi chỉ tiêu xét riêng; nhóm ngừng dùng bị bỏ qua; nguồn được trả về để hiển thị.
/// </summary>
public static class GroupDefaults
{
    public static async Task<PartnerTerms> PartnerTermsAsync(HakDbContext db, Guid companyId, BusinessPartner partner)
    {
        if (partner.CreditLimit is not null && partner.PaymentTermsDays is not null)
            return new PartnerTerms(partner.CreditLimit, partner.PaymentTermsDays, "đối tượng", "đối tượng");
        var memberOf = await db.CatalogGroupMembers.AsNoTracking().Where(m => m.EntityId == partner.Id && m.CompanyId == companyId).Select(m => m.GroupId).ToListAsync();
        if (memberOf.Count == 0) return new PartnerTerms(partner.CreditLimit, partner.PaymentTermsDays, partner.CreditLimit is null ? null : "đối tượng", partner.PaymentTermsDays is null ? null : "đối tượng");
        var all = await db.CatalogGroups.AsNoTracking().Where(g => g.CompanyId == companyId && g.Domain == GroupDomain.Partner).ToDictionaryAsync(g => g.Id);
        int Depth(CatalogGroup g) { var d = 0; for (var c = g.ParentId; c is not null && all.TryGetValue(c.Value, out var p); c = p.ParentId) d++; return d; }
        var mine = memberOf.Where(all.ContainsKey).Select(id => all[id]).Where(g => g.IsActive).ToList();

        (T? value, string? src) Pick<T>(T? own, Func<CatalogGroup, T?> get, Func<IEnumerable<T>, T> strictest) where T : struct
        {
            if (own is not null) return (own, "đối tượng");
            var withValue = mine.Where(g => get(g) is not null).ToList();
            if (withValue.Count == 0) return (null, null);
            var deepest = withValue.Max(Depth);
            var top = withValue.Where(g => Depth(g) == deepest).ToList();
            return (strictest(top.Select(g => get(g)!.Value)), "nhóm " + string.Join(" / ", top.OrderBy(g => g.Code, StringComparer.Ordinal).Select(g => g.Code)));
        }
        var limit = Pick<decimal>(partner.CreditLimit, g => g.DefaultCreditLimit, v => v.Min());
        var days = Pick<int>(partner.PaymentTermsDays, g => g.DefaultPaymentTermsDays, v => v.Min());
        return new PartnerTerms(limit.value, days.value, limit.src, days.src);
    }
}
