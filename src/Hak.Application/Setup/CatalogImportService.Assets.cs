using Hak.Application.Assets;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Domain.Assets;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

/// <summary>One asset / tool card taken over from the previous system.</summary>
public sealed record AssetImportRow(int Line, string? Code, string? Name, string? Class = null, string? AssetAccount = null, string? AccumulatedAccount = null,
    string? ExpenseAccount = null, string? OriginalCost = null, string? OpeningAccumulated = null, string? UsefulLifeMonths = null,
    string? RemainingLifeMonths = null, string? InServiceDate = null, string? Proration = null, string? ResidualValue = null);

public sealed partial class CatalogImportService
{
    /// <summary>
    /// Opening fixed assets / tools. Cost cannot be typed in: each card's original cost is allocated from the debit lines on its
    /// asset account in the POSTED opening-balance voucher <paramref name="openingVoucherNumber"/> (the register and the ledger
    /// can never disagree). Accumulated depreciation is carried as the card's opening accumulated value; depreciation continues
    /// from the voucher's posting date over the remaining months — never catches up past periods. Cards are created as Drafts.
    /// </summary>
    public async Task<ImportResult> ImportAssetsAsync(SessionContext ctx, string openingVoucherNumber, IReadOnlyList<AssetImportRow> rows, bool commit)
    {
        ctx.Require(Permissions.AssetManage);
        RequireSize(rows.Count);
        var issues = new List<ImportIssue>();
        var notes = new List<string>();
        var number = openingVoucherNumber.Trim();
        var voucher = await db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.CompanyId == ctx.CompanyId && v.Number == number);
        if (voucher is null) issues.Add(new(0, "Chứng từ OPB", $"Không có chứng từ {number}."));
        else if (voucher.Status != VoucherStatus.Posted) issues.Add(new(0, "Chứng từ OPB", $"Chứng từ {number} chưa ghi sổ (hoặc đã đảo); ghi sổ số dư đầu kỳ trước."));
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Code, StringComparer.Ordinal);
        var existing = (await db.LongTermAssets.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).Select(a => a.Code).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var functional = await db.Companies.Where(c => c.Id == ctx.CompanyId).Select(c => c.FunctionalCurrency).FirstAsync();
        var dp = await db.Currencies.Where(c => c.Code == functional).Select(c => c.DecimalPlaces).FirstAsync();

        // Debit lines of the opening voucher per asset account, with what other cards (any status but cancelled) already claim.
        var pools = new Dictionary<Guid, List<(Guid LineId, decimal Free)>>();
        var credits = new Dictionary<Guid, decimal>();
        if (voucher is { Status: VoucherStatus.Posted })
        {
            var lines = await (from l in db.JournalLines
                               join e in db.JournalEntries on l.JournalEntryId equals e.Id
                               where e.VoucherId == voucher.Id
                               orderby l.LineNo
                               select new { l.Id, l.AccountId, l.Side, l.Amount }).AsNoTracking().ToListAsync();
            var ids = lines.Select(l => l.Id).ToList();
            var claimed = await (from s in db.AssetCostSources
                                 join a in db.LongTermAssets on s.AssetId equals a.Id
                                 where ids.Contains(s.JournalLineId) && a.Status != AssetStatus.Cancelled
                                 group s by s.JournalLineId into g
                                 select new { g.Key, Total = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total);
            foreach (var l in lines.Where(l => l.Side == Side.Debit))
            {
                if (!pools.TryGetValue(l.AccountId, out var pool)) pools[l.AccountId] = pool = [];
                pool.Add((l.Id, l.Amount - claimed.GetValueOrDefault(l.Id)));
            }
            foreach (var l in lines.Where(l => l.Side == Side.Credit)) credits[l.AccountId] = credits.GetValueOrDefault(l.AccountId) + l.Amount;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var valid = new List<(int Line, AssetInput Input)>();
        var accumulatedTotals = new Dictionary<Guid, decimal>();
        foreach (var r in rows)
        {
            var before = issues.Count;
            var code = Norm(r.Code)?.ToUpperInvariant();
            if (code is null) { issues.Add(new(r.Line, "Mã", "Thiếu mã tài sản.")); continue; }
            if (seen.TryGetValue(code, out var first)) { issues.Add(new(r.Line, "Mã", $"Mã {code} trùng với dòng {first}.")); continue; }
            seen[code] = r.Line;
            if (existing.Contains(code)) { issues.Add(new(r.Line, "Mã", $"Tài sản {code} đã có thẻ.")); continue; }
            var name = Norm(r.Name);
            if (name is null) issues.Add(new(r.Line, "Tên", "Thiếu tên tài sản."));
            var cls = Choice(r.Class, AssetClass.FixedAsset, r.Line, "Loại", issues,
                (AssetClass.FixedAsset, ["TSCĐ", "TSCD", "TAI SAN CO DINH", "TÀI SẢN CỐ ĐỊNH", "FIXEDASSET", "FA"]),
                (AssetClass.Tool, ["CCDC", "CONG CU", "CÔNG CỤ", "CÔNG CỤ DỤNG CỤ", "CHI PHÍ TRẢ TRƯỚC", "TOOL"]));
            var proration = Choice(r.Proration, ProrationPolicy.FullMonth, r.Line, "Cách tính tháng", issues,
                (ProrationPolicy.FullMonth, ["FULLMONTH", "TRÒN THÁNG", "TRON THANG", "THÁNG"]),
                (ProrationPolicy.Daily, ["DAILY", "THEO NGÀY", "THEO NGAY", "NGÀY"]));
            Guid? Acc(string? raw, string field, bool required)
            {
                var c = Norm(raw)?.ToUpperInvariant();
                if (c is null) { if (required) issues.Add(new(r.Line, field, $"Thiếu {field}.")); return null; }
                if (!accounts.TryGetValue(c, out var a)) { issues.Add(new(r.Line, field, $"TK {c} không tồn tại.")); return null; }
                if (!a.IsPostable || !a.IsActive) { issues.Add(new(r.Line, field, $"TK {c} phải là TK chi tiết đang dùng.")); return null; }
                return a.Id;
            }
            var assetAcc = Acc(r.AssetAccount, "TK tài sản", true);
            var accumulatedAcc = Acc(r.AccumulatedAccount, "TK hao mòn", cls == AssetClass.FixedAsset);
            var expenseAcc = Acc(r.ExpenseAccount, "TK chi phí", true);
            if (cls == AssetClass.Tool && accumulatedAcc is not null) issues.Add(new(r.Line, "TK hao mòn", "CCDC phân bổ thẳng vào TK của nó; để trống TK hao mòn."));
            var parse = issues.Count;
            var cost = Amount(r.OriginalCost, r.Line, "Nguyên giá", issues);
            var accumulated = Amount(r.OpeningAccumulated, r.Line, "Hao mòn lũy kế", issues);
            var residual = Amount(r.ResidualValue, r.Line, "Giá trị thu hồi", issues);
            var life = Amount(r.UsefulLifeMonths, r.Line, "Số tháng sử dụng", issues);
            var remaining = Amount(r.RemainingLifeMonths, r.Line, "Số tháng còn lại", issues);
            DateOnly inService = default;
            if (Norm(r.InServiceDate) is not { } d) issues.Add(new(r.Line, "Ngày sử dụng", "Thiếu ngày đưa vào sử dụng."));
            else if (!TryDate(d, out inService)) issues.Add(new(r.Line, "Ngày sử dụng", $"Ngày '{d}' không hợp lệ (yyyy-MM-dd hoặc dd/MM/yyyy)."));
            if (issues.Count == parse)
            {
                if (cost <= 0 || !MoneyMath.HasAtMostDecimals(cost, dp)) issues.Add(new(r.Line, "Nguyên giá", $"Nguyên giá phải dương, tối đa {dp} chữ số thập phân."));
                if (cls == AssetClass.Tool && accumulated != 0)
                    issues.Add(new(r.Line, "Hao mòn lũy kế", "CCDC: TK của CCDC chỉ còn giá trị chưa phân bổ — nhập giá trị còn lại vào cột Nguyên giá, hao mòn để 0."));
                else if (accumulated < 0 || residual < 0 || accumulated + residual > cost) issues.Add(new(r.Line, "Hao mòn lũy kế", "Hao mòn lũy kế, giá trị thu hồi không âm và cộng lại không vượt nguyên giá."));
                if (life <= 0 || life != decimal.Truncate(life)) issues.Add(new(r.Line, "Số tháng sử dụng", "Số tháng sử dụng là số nguyên dương."));
                if (remaining <= 0 || remaining != decimal.Truncate(remaining) || remaining > life)
                    issues.Add(new(r.Line, "Số tháng còn lại", "Số tháng còn lại là số nguyên dương, không quá số tháng sử dụng (bắt buộc với tài sản chuyển sang)."));
                if (voucher is not null && inService > voucher.PostingDate) issues.Add(new(r.Line, "Ngày sử dụng", "Ngày đưa vào sử dụng sau ngày số dư đầu kỳ — đây không phải tài sản đầu kỳ."));
            }
            if (issues.Count > before || assetAcc is null || expenseAcc is null || voucher is not { Status: VoucherStatus.Posted }) continue;

            // Allocate the cost from the opening voucher's debit lines on this asset account, in line order.
            var sources = new List<AssetCostSourceInput>();
            var need = cost;
            foreach (var i in Enumerable.Range(0, pools.GetValueOrDefault(assetAcc.Value)?.Count ?? 0))
            {
                var pool = pools[assetAcc.Value];
                var take = Math.Min(need, pool[i].Free);
                if (take <= 0) continue;
                sources.Add(new AssetCostSourceInput(pool[i].LineId, take));
                pool[i] = (pool[i].LineId, pool[i].Free - take);
                need -= take;
                if (need == 0) break;
            }
            if (need > 0)
            {
                issues.Add(new(r.Line, "Nguyên giá", $"Chứng từ {number} không còn đủ phát sinh Nợ TK {Norm(r.AssetAccount)!.ToUpperInvariant()} cho nguyên giá này (thiếu {VietnameseNumber(need)})."));
                continue;
            }
            if (accumulatedAcc is { } acc) accumulatedTotals[acc] = accumulatedTotals.GetValueOrDefault(acc) + accumulated;
            valid.Add((r.Line, new AssetInput(code, name!, cls, assetAcc.Value, accumulatedAcc, expenseAcc.Value, proration, residual, (int)life, inService,
                sources, accumulated, (int)remaining, voucher.PostingDate, "Chuyển từ hệ thống cũ")));
        }
        if (valid.Count == 0 && issues.Count == 0) issues.Add(new(0, "Tổng", "Không có tài sản nào."));
        var byId = accounts.Values.ToDictionary(a => a.Id);
        foreach (var (acc, total) in accumulatedTotals)
            if (credits.GetValueOrDefault(acc) != total)
                notes.Add($"Tổng hao mòn lũy kế nhập {VietnameseNumber(total)} khác số dư Có TK {byId[acc].Code} trên {number} ({VietnameseNumber(credits.GetValueOrDefault(acc))}) — kiểm tra lại nếu toàn bộ tài sản đều được nhập.");
        foreach (var (acc, pool) in pools.Where(p => valid.Any(v => v.Input.AssetAccountId == p.Key)))
            if (pool.Sum(p => p.Free) is var left && left > 0)
                notes.Add($"TK {byId[acc].Code} trên {number} còn {VietnameseNumber(left)} nguyên giá chưa gán cho thẻ nào.");

        var result = new ImportResult(false, valid.Count, 0, Sorted(issues), notes, TotalDebit: valid.Sum(v => v.Input.CostSources.Sum(s => s.Amount)),
            TotalCredit: valid.Sum(v => v.Input.OpeningAccumulated));
        if (!commit || issues.Count > 0) return result;
        await db.InTransactionAsync(async () =>
        {
            foreach (var v in valid) await WithLine(v.Line, () => assets.CreateAsync(ctx, v.Input));
            await audit.AppendAsync(ctx.CompanyId, ctx, "import.assets", "company", ctx.CompanyId.ToString(), new { number, created = valid.Count });
            await db.SaveChangesAsync();
        });
        return result with { Committed = true };
    }
}
