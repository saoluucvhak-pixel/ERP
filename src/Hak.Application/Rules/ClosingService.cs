using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Rules;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Rules;

public sealed record ClosingStepInput(string Description, string SourcePrefixes, string TargetAccountCode);

/// <summary>
/// GenerateClosingEntries (docs/03 §3). Produces one CLS Draft voucher per period from the active,
/// versioned closing rule set. Idempotent: the voucher carries source HAK.CLOSING/&lt;period id&gt;, so a
/// second run is refused while one exists (cancel the draft or reverse the posted one to regenerate).
/// </summary>
public sealed class ClosingService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string SourceSystem = "HAK.CLOSING";

    public async Task<ClosingRuleSet> CreateRuleSetAsync(SessionContext ctx, string code, DateOnly effectiveFrom, DateOnly? effectiveTo,
        IReadOnlyList<ClosingStepInput> steps, string? description = null)
    {
        ctx.Require(Permissions.ConfigManage);
        if (steps.Count == 0) throw new DomainException(ErrorCodes.RuleInvalid, "Bộ quy tắc kết chuyển phải có ít nhất một bước.");
        return await db.InTransactionAsync(async () =>
        {
            var version = (await db.ClosingRuleSets.Where(r => r.CompanyId == ctx.CompanyId && r.Code == code).MaxAsync(r => (int?)r.Version) ?? 0) + 1;
            var set = new ClosingRuleSet
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Code = code, Version = version, EffectiveFrom = effectiveFrom, EffectiveTo = effectiveTo,
                Description = description, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            var seq = 0;
            foreach (var s in steps)
            {
                var target = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == ctx.CompanyId && a.Code == s.TargetAccountCode)
                             ?? throw new DomainException(ErrorCodes.AccountNotFound, $"Tài khoản đích {s.TargetAccountCode} không tồn tại.");
                if (!target.IsPostable) throw new DomainException(ErrorCodes.AccountNotPostable, $"Tài khoản đích {target.Code} không phải tài khoản chi tiết.");
                var prefixes = Prefixes(s.SourcePrefixes);
                if (prefixes.Count == 0) throw new DomainException(ErrorCodes.RuleInvalid, $"Bước '{s.Description}' thiếu tài khoản nguồn.");
                if (prefixes.Any(p => target.Code.StartsWith(p, StringComparison.Ordinal)))
                    throw new DomainException(ErrorCodes.RuleInvalid, $"Bước '{s.Description}': tài khoản đích nằm trong nhóm nguồn.");
                set.Steps.Add(new ClosingStep { Id = Ids.New(), RuleSetId = set.Id, Sequence = ++seq, Description = s.Description, SourcePrefixes = string.Join(",", prefixes), TargetAccountId = target.Id });
            }
            db.ClosingRuleSets.Add(set);
            await audit.AppendAsync(ctx.CompanyId, ctx, "closing.ruleset.create", "closing_rule_set", set.Id.ToString(), new { code, version, steps });
            await db.SaveChangesAsync();
            return set;
        });
    }

    public async Task ActivateRuleSetAsync(SessionContext ctx, Guid ruleSetId)
    {
        ctx.Require(Permissions.ConfigManage);
        await db.InTransactionAsync(async () =>
        {
            var set = await db.ClosingRuleSets.FirstOrDefaultAsync(r => r.Id == ruleSetId && r.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Bộ quy tắc kết chuyển không tồn tại.");
            if (set.Status != RuleStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ kích hoạt bộ quy tắc ở trạng thái Draft.");
            foreach (var old in await db.ClosingRuleSets.Where(r => r.CompanyId == ctx.CompanyId && r.Code == set.Code && r.Status == RuleStatus.Active).ToListAsync())
                old.Status = RuleStatus.Retired;
            set.Status = RuleStatus.Active;
            set.ActivatedAt = clock.UtcNow;
            set.ActivatedBy = ctx.UserId;
            await audit.AppendAsync(ctx.CompanyId, ctx, "closing.ruleset.activate", "closing_rule_set", set.Id.ToString(), new { set.Code, set.Version });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Computes the closing lines without writing anything.</summary>
    public async Task<(ClosingRuleSet RuleSet, List<VoucherLineInput> Lines)> PreviewAsync(SessionContext ctx, Guid periodId)
    {
        ctx.Require(Permissions.VoucherView);
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == ctx.CompanyId)
                     ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại.");
        var sets = await db.ClosingRuleSets.AsNoTracking().Include(r => r.Steps)
            .Where(r => r.CompanyId == ctx.CompanyId && r.Status == RuleStatus.Active && r.EffectiveFrom <= period.EndDate
                        && (r.EffectiveTo == null || r.EffectiveTo >= period.EndDate)).ToListAsync();
        if (sets.Count != 1)
            throw new DomainException(sets.Count == 0 ? ErrorCodes.RuleMissing : ErrorCodes.RuleAmbiguous,
                sets.Count == 0 ? "Chưa có bộ quy tắc kết chuyển đang hiệu lực." : "Có nhiều bộ quy tắc kết chuyển cùng hiệu lực.");
        var set = sets[0];

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.IsPostable).ToListAsync();
        var sums = await db.JournalLines.Where(l => l.CompanyId == ctx.CompanyId && l.PostingDate <= period.EndDate)
            .GroupBy(l => new { l.AccountId, l.PartnerId, l.Side }).Select(g => new { g.Key.AccountId, g.Key.PartnerId, g.Key.Side, Total = g.Sum(x => x.Amount) })
            .ToListAsync();
        // Signed balance per (account, partner), updated as earlier steps move amounts into later steps' sources.
        var bal = sums.GroupBy(s => (s.AccountId, s.PartnerId)).ToDictionary(g => g.Key, g => g.Sum(s => (int)s.Side * s.Total));

        var lines = new List<VoucherLineInput>();
        foreach (var step in set.Steps.OrderBy(s => s.Sequence))
        {
            var prefixes = Prefixes(step.SourcePrefixes);
            var sources = accounts.Where(a => a.Id != step.TargetAccountId && prefixes.Any(p => a.Code.StartsWith(p, StringComparison.Ordinal)))
                .OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
            foreach (var acc in sources)
            {
                foreach (var key in bal.Keys.Where(k => k.AccountId == acc.Id).OrderBy(k => k.PartnerId).ToList())
                {
                    var b = bal[key];
                    if (b == 0) continue;
                    var desc = $"{step.Description}: {acc.Code}";
                    lines.Add(new VoucherLineInput(acc.Id, b > 0 ? Side.Credit : Side.Debit, Math.Abs(b), key.PartnerId, Description: desc));
                    lines.Add(new VoucherLineInput(step.TargetAccountId, b > 0 ? Side.Debit : Side.Credit, Math.Abs(b), Description: desc));
                    bal[key] = 0;
                    var tKey = (step.TargetAccountId, (Guid?)null);
                    bal[tKey] = bal.GetValueOrDefault(tKey) + b;
                }
            }
        }
        return (set, lines);
    }

    public async Task<Voucher> GenerateAsync(SessionContext ctx, Guid periodId)
    {
        ctx.Require(Permissions.VoucherCreate);
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == ctx.CompanyId)
                     ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại.");
        var existing = await db.Vouchers.AsNoTracking()
            .FirstOrDefaultAsync(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == SourceSystem && v.SourceId == periodId.ToString() && v.Status != VoucherStatus.Cancelled);
        if (existing is not null)
            throw new DomainException(existing.Status is VoucherStatus.Posted ? ErrorCodes.AlreadyClosed : ErrorCodes.DuplicateSource,
                $"Kỳ đã có chứng từ kết chuyển {existing.Number} ({existing.Status}). Hủy nháp hoặc đảo chứng từ đó trước khi tạo lại.");
        var pending = await db.Vouchers.CountAsync(v => v.CompanyId == ctx.CompanyId && v.PostingDate >= period.StartDate && v.PostingDate <= period.EndDate
                                                       && (v.Status == VoucherStatus.Submitted || v.Status == VoucherStatus.Approved));
        if (pending > 0)
            throw new DomainException(ErrorCodes.PeriodHasPending, $"Còn {pending} chứng từ đã gửi/đã duyệt chưa ghi sổ trong kỳ; xử lý trước khi kết chuyển.");

        var (set, lines) = await PreviewAsync(ctx, periodId);
        if (lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Không có số dư cần kết chuyển.");
        return await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.Closing, period.EndDate, period.EndDate,
            $"Kết chuyển cuối kỳ {period.StartDate:MM/yyyy} theo {set.Code} v{set.Version}", lines,
            SourceSystem: SourceSystem, SourceId: periodId.ToString(), SourceVersion: $"{set.Code}:{set.Version}"));
    }

    private static List<string> Prefixes(string s) => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
}
