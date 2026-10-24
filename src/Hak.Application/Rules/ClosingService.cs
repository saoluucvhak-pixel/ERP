using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Organization;
using Hak.Domain.Rules;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Rules;

public sealed record ClosingStepInput(string Description, string SourcePrefixes, string TargetAccountCode);

public sealed record ClosingStepInfo(int Sequence, string Description, string SourcePrefixes, string TargetAccountCode);

public sealed record ClosingRuleSetInfo(Guid Id, string Code, int Version, string Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Description,
    IReadOnlyList<ClosingStepInfo> Steps);

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

    /// <summary>All closing rule sets of the company (newest version first) with their steps, for the setup screen.</summary>
    public async Task<IReadOnlyList<ClosingRuleSetInfo>> ListRuleSetsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.VoucherView);
        var sets = await db.ClosingRuleSets.AsNoTracking().Include(r => r.Steps).Where(r => r.CompanyId == ctx.CompanyId)
            .OrderBy(r => r.Code).ThenByDescending(r => r.Version).ToListAsync();
        var accountIds = sets.SelectMany(r => r.Steps).Select(x => x.TargetAccountId).Distinct().ToList();
        var codes = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code);
        return sets.Select(r => new ClosingRuleSetInfo(r.Id, r.Code, r.Version, r.Status.ToString(), r.EffectiveFrom, r.EffectiveTo, r.Description,
            r.Steps.OrderBy(x => x.Sequence).Select(x => new ClosingStepInfo(x.Sequence, x.Description, x.SourcePrefixes, codes.GetValueOrDefault(x.TargetAccountId, "?"))).ToList())).ToList();
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
        return await ComputeAsync(db, ctx.CompanyId, period);
    }

    /// <summary>
    /// Kiểm tra chứng từ kết chuyển còn Nháp / Đã gửi / Đã duyệt vẫn KHỚP số dư hiện tại của tập nguồn (tính lại theo bộ quy tắc đang hiệu lực).
    /// Nếu sau khi tạo nháp có chứng từ khác được ghi sổ trong kỳ (hoặc bộ quy tắc đổi), việc duyệt / ghi sổ bị chặn: hủy nháp rồi tạo lại.
    /// </summary>
    public static async Task CheckVoucherAsync(HakDbContext db, Voucher v, List<ValidationError> errors)
    {
        if (v.SourceSystem != SourceSystem || v.Status is not (VoucherStatus.Draft or VoucherStatus.Submitted or VoucherStatus.Approved)) return;
        var id = v.SourceId?.Split('#')[0];
        var period = Guid.TryParse(id, out var pid)
            ? await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.CompanyId == v.CompanyId) : null;
        if (period is null) { errors.Add(new(ErrorCodes.PeriodNotFound, "Không tìm thấy kỳ của chứng từ kết chuyển.")); return; }
        List<VoucherLineInput> expected;
        try { expected = (await ComputeAsync(db, v.CompanyId, period)).Lines; }
        catch (DomainException e) { errors.Add(new(e.Code, e.Message)); return; }
        static string Key(Guid account, Guid? partner, Side side, decimal amount) => $"{account}|{partner}|{(int)side}|{amount:0.0000}";
        var want = expected.Select(l => Key(l.AccountId, l.PartnerId, l.Side, l.Amount)).Order(StringComparer.Ordinal).ToList();
        var have = v.Lines.Select(l => Key(l.AccountId, l.PartnerId, l.Side, l.Amount)).Order(StringComparer.Ordinal).ToList();
        if (!want.SequenceEqual(have))
            errors.Add(new(ErrorCodes.ClosingOutOfDate, $"Chứng từ kết chuyển {v.Number} không còn khớp số dư hiện tại của kỳ (có biến động sau khi tạo nháp hoặc bộ quy tắc đã đổi). Hủy nháp và tạo lại."));
    }

    private static async Task<(ClosingRuleSet RuleSet, List<VoucherLineInput> Lines)> ComputeAsync(HakDbContext db, Guid companyId, AccountingPeriod period)
    {
        var sets = await db.ClosingRuleSets.AsNoTracking().Include(r => r.Steps)
            .Where(r => r.CompanyId == companyId && r.Status == RuleStatus.Active && r.EffectiveFrom <= period.EndDate
                        && (r.EffectiveTo == null || r.EffectiveTo >= period.EndDate)).ToListAsync();
        if (sets.Count != 1)
            throw new DomainException(sets.Count == 0 ? ErrorCodes.RuleMissing : ErrorCodes.RuleAmbiguous,
                sets.Count == 0 ? "Chưa có bộ quy tắc kết chuyển đang hiệu lực." : "Có nhiều bộ quy tắc kết chuyển cùng hiệu lực.");
        var set = sets[0];

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.IsPostable).ToListAsync();
        var sums = await db.JournalLines.Where(l => l.CompanyId == companyId && l.PostingDate <= period.EndDate)
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
        // Chứng từ kết chuyển đã bị đảo thì kỳ được kết chuyển lại (chị chốt 21/10/2026). Khóa nguồn của bản đã đảo bị chiếm, nên bản mới mang hậu tố #n.
        var key = periodId.ToString();
        var history = await db.Vouchers.AsNoTracking()
            .Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == SourceSystem && (v.SourceId == key || v.SourceId!.StartsWith(key + "#")) && v.Status != VoucherStatus.Cancelled)
            .ToListAsync();
        var existing = history.FirstOrDefault(v => v.Status != VoucherStatus.Reversed);
        var sourceId = history.Count == 0 ? key : $"{key}#{history.Count + 1}";
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
            SourceSystem: SourceSystem, SourceId: sourceId, SourceVersion: $"{set.Code}:{set.Version}"));
    }

    private static List<string> Prefixes(string s) => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
}
