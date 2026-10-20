using Hak.Application.Audit;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Common;

public sealed record NumberingRow(string Key, string Name, string Cycle, string DefaultPrefix, string Prefix, int Width, bool Customized, string Example, long LastIssued);

/// <summary>Xem và tùy chỉnh bộ mã chứng từ của công ty (quy tắc ở docs/11-QUY-TAC-MA.md).</summary>
public sealed class NumberingService(HakDbContext db, AuditService audit, IClock clock)
{
    public async Task<IReadOnlyList<NumberingRow>> ListAsync(SessionContext ctx, DateOnly today)
    {
        ctx.Require(Permissions.VoucherView);
        var rules = await db.NumberingRules.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId).ToDictionaryAsync(r => r.Key);
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == ctx.CompanyId && f.StartDate <= today && f.EndDate >= today);
        var last = fy is null ? new Dictionary<string, long>() : await db.VoucherNumberSequences.AsNoTracking().Where(s => s.CompanyId == ctx.CompanyId && s.FiscalYearId == fy.Id).ToDictionaryAsync(s => s.VoucherType, s => s.LastNumber);
        return DocumentKinds.All.Select(k =>
        {
            rules.TryGetValue(k.Key, out var r);
            var prefix = r?.Prefix ?? k.Prefix; var width = r?.Width ?? DocumentKinds.DefaultWidth;
            return new NumberingRow(k.Key, k.Name, k.Cycle, k.Prefix, prefix, width, r is { Prefix: not null } or { Width: not null },
                Numbering.Format(prefix, fy?.Code ?? "2026", last.GetValueOrDefault(k.Key) + 1, width), last.GetValueOrDefault(k.Key));
        }).ToList();
    }

    /// <summary>Đặt tiền tố / độ rộng cho một loại (null = về mặc định). Tiền tố phải đúng quy tắc, khác mọi tiền tố đang dùng và không là phần đầu của tiền tố khác (và ngược lại).</summary>
    public async Task SetAsync(SessionContext ctx, string key, string? prefix, int? width)
    {
        ctx.Require(Permissions.CatalogManage);
        var kind = DocumentKinds.All.FirstOrDefault(k => k.Key == key) ?? throw new DomainException(ErrorCodes.NotFound, "Loại chứng từ không tồn tại.");
        prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim().ToUpperInvariant();
        if (prefix is not null && DocumentKinds.PrefixProblem(prefix) is { } problem) throw new DomainException(ErrorCodes.ValidationFailed, problem);
        if (width is < 4 or > 8) throw new DomainException(ErrorCodes.ValidationFailed, "Độ rộng số thứ tự từ 4 đến 8 chữ số.");
        await db.InTransactionAsync(async () =>
        {
            var rules = await db.NumberingRules.Where(r => r.CompanyId == ctx.CompanyId).ToListAsync();
            if (prefix is not null)
            {
                var others = DocumentKinds.All.Where(k => k.Key != key).Select(k => (k, p: rules.FirstOrDefault(r => r.Key == k.Key)?.Prefix ?? k.Prefix)).ToList();
                var clash = others.FirstOrDefault(o => o.p.StartsWith(prefix, StringComparison.Ordinal) || prefix.StartsWith(o.p, StringComparison.Ordinal));
                if (clash.k is not null)
                    throw new DomainException(ErrorCodes.DuplicateCode, $"Tiền tố {prefix} trùng hoặc dễ nhầm với {clash.p} ({clash.k.Name}); chọn tiền tố khác.");
            }
            var rule = rules.FirstOrDefault(r => r.Key == key);
            if (prefix is null && width is null)
            {
                if (rule is not null) db.NumberingRules.Remove(rule);
            }
            else
            {
                if (rule is null) db.NumberingRules.Add(rule = new NumberingRule { Id = Ids.New(), CompanyId = ctx.CompanyId, Key = key });
                rule.Prefix = prefix; rule.Width = width; rule.UpdatedAt = clock.UtcNow; rule.UpdatedBy = ctx.UserId;
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "numbering.set", "numbering_rule", key, new { kind.Name, prefix, width });
            await db.SaveChangesAsync();
        });
    }
}
