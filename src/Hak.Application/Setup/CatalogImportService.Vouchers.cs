using Hak.Application.Accounting;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

/// <summary>
/// One line of a voucher file (migration of transactions from another system): lines with the same <see cref="Key"/> form one
/// voucher. Number / description of the source voucher are kept in the description.
/// </summary>
public sealed record VoucherImportRow(int Line, string? Key, string? Date, string? Number = null, string? Description = null, string? AccountCode = null,
    string? PartnerCode = null, string? Debit = null, string? Credit = null, string? Currency = null, string? ForeignAmount = null, string? LineDescription = null,
    string? Type = null);

/// <summary>Result of a bulk submit / approve-and-post of imported vouchers.</summary>
public sealed record ImportPostResult(int Done, IReadOnlyList<string> Failures);

public sealed partial class CatalogImportService
{
    /// <summary>Source system of imported vouchers; the source id is the file's voucher key, so the same voucher is never imported twice.</summary>
    public const string ImportSource = "HAK.IMPORT";

    /// <summary>
    /// Validates the whole file and (commit) creates one General-journal Draft per key — all or nothing. Every voucher then goes
    /// through submit → approve → post like any other (bulk actions below keep maker ≠ checker).
    /// </summary>
    public async Task<ImportResult> ImportVouchersAsync(SessionContext ctx, IReadOnlyList<VoucherImportRow> rows, bool commit)
    {
        ctx.Require(Permissions.VoucherCreate);
        ctx.Require(Permissions.DataImport);
        RequireSize(rows.Count);
        var issues = new List<ImportIssue>();
        var notes = new List<string>();
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Code, StringComparer.Ordinal);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Code, StringComparer.Ordinal);
        var functional = await db.Companies.Where(c => c.Id == ctx.CompanyId).Select(c => c.FunctionalCurrency).FirstAsync();
        var currencies = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.DecimalPlaces, StringComparer.Ordinal);
        var dp = currencies[functional];
        var years = await db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == ctx.CompanyId).Select(y => new { y.StartDate, y.EndDate }).ToListAsync();
        var existing = (await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == ImportSource && v.Status != VoucherStatus.Cancelled)
            .Select(v => v.SourceId!).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        var groups = new List<(string Key, int FirstLine, DateOnly Date, string Description, string Type, List<VoucherLineInput> Lines)>();
        foreach (var g in rows.GroupBy(r => Norm(r.Key) ?? "").OrderBy(g => g.Min(r => r.Line)))
        {
            var first = g.OrderBy(r => r.Line).First();
            if (g.Key.Length == 0) { foreach (var r in g) issues.Add(new(r.Line, "Mã chứng từ", "Thiếu mã chứng từ (các dòng cùng mã là một chứng từ).")); continue; }
            if (g.Key.Length > 100) { issues.Add(new(first.Line, "Mã chứng từ", "Mã chứng từ tối đa 100 ký tự.")); continue; }
            if (existing.Contains(g.Key)) { issues.Add(new(first.Line, "Mã chứng từ", $"Chứng từ {g.Key} đã được nhập trước đây.")); continue; }
            var before = issues.Count;
            var dates = g.Select(r => Norm(r.Date)).Where(d => d is not null).Distinct().ToList();
            DateOnly date = default;
            if (dates.Count != 1) issues.Add(new(first.Line, "Ngày", dates.Count == 0 ? "Thiếu ngày chứng từ." : $"Các dòng của chứng từ {g.Key} có nhiều ngày khác nhau."));
            else if (!TryDate(dates[0]!, out date)) issues.Add(new(first.Line, "Ngày", $"Ngày '{dates[0]}' không hợp lệ (dùng yyyy-MM-dd hoặc dd/MM/yyyy)."));
            else if (!years.Any(y => y.StartDate <= date && date <= y.EndDate)) issues.Add(new(first.Line, "Ngày", $"Ngày {date:dd/MM/yyyy} chưa có năm tài chính."));
            // Voucher type: general journal (default) or closing entry (kết chuyển, CLS — left out of income-statement movements).
            var types = g.Select(r => Norm(r.Type)?.ToUpperInvariant()).Where(x => x is not null).Distinct().ToList();
            var type = VoucherTypes.General;
            if (types.Count > 1) issues.Add(new(first.Line, "Loại chứng từ", $"Các dòng của chứng từ {g.Key} có nhiều loại khác nhau."));
            else if (types.Count == 1)
            {
                type = types[0] switch { "GJ" or "CHUNG" or "TONG HOP" => VoucherTypes.General, "CLS" or "KET CHUYEN" or "KẾT CHUYỂN" => VoucherTypes.Closing, _ => "" };
                if (type.Length == 0) issues.Add(new(first.Line, "Loại chứng từ", $"Loại '{types[0]}' không hỗ trợ (GJ hoặc CLS – kết chuyển)."));
            }
            var number = g.Select(r => Norm(r.Number)).FirstOrDefault(x => x is not null);
            var text = g.Select(r => Norm(r.Description)).FirstOrDefault(x => x is not null);
            var description = $"{(number is null ? "" : $"[{number}] ")}{text ?? "Chứng từ chuyển từ hệ thống cũ"}";

            var lines = new List<VoucherLineInput>();
            foreach (var r in g.OrderBy(r => r.Line))
            {
                var lineBefore = issues.Count;
                var code = Norm(r.AccountCode)?.ToUpperInvariant();
                Account? acc = null;
                if (code is null) issues.Add(new(r.Line, "Mã TK", "Thiếu mã tài khoản."));
                else if (!accounts.TryGetValue(code, out acc)) issues.Add(new(r.Line, "Mã TK", $"TK {code} không tồn tại."));
                else if (!acc.IsPostable) issues.Add(new(r.Line, "Mã TK", $"TK {code} là TK tổng hợp."));
                else if (!acc.IsActive) issues.Add(new(r.Line, "Mã TK", $"TK {code} đã ngừng dùng."));
                var partnerCode = Norm(r.PartnerCode)?.ToUpperInvariant();
                Domain.Catalog.BusinessPartner? partner = null;
                if (partnerCode is not null && !partners.TryGetValue(partnerCode, out partner)) issues.Add(new(r.Line, "Đối tượng", $"Đối tượng {partnerCode} không tồn tại."));
                if (acc is { RequiresPartner: true } && partnerCode is null) issues.Add(new(r.Line, "Đối tượng", $"TK {code} bắt buộc đối tượng."));
                var debit = Amount(r.Debit, r.Line, "Nợ", issues);
                var credit = Amount(r.Credit, r.Line, "Có", issues);
                if (debit < 0 || credit < 0) issues.Add(new(r.Line, "Số tiền", "Số tiền không được âm."));
                else if ((debit > 0) == (credit > 0)) issues.Add(new(r.Line, "Số tiền", "Mỗi dòng ghi đúng một bên: Nợ hoặc Có."));
                var amount = debit > 0 ? debit : credit;
                if (amount > 0 && !MoneyMath.HasAtMostDecimals(amount, dp)) issues.Add(new(r.Line, "Số tiền", $"Số tiền {functional} tối đa {dp} chữ số thập phân."));
                var currency = Norm(r.Currency)?.ToUpperInvariant();
                if (currency == functional) currency = null;
                decimal? foreign = null, rate = null;
                if (currency is not null)
                {
                    foreign = Amount(r.ForeignAmount, r.Line, "Nguyên tệ", issues);
                    if (!currencies.ContainsKey(currency)) issues.Add(new(r.Line, "Loại tiền", $"Loại tiền {currency} chưa khai báo."));
                    else if (foreign <= 0) issues.Add(new(r.Line, "Nguyên tệ", "Dòng ngoại tệ phải có số nguyên tệ dương."));
                    else if (amount > 0 && (rate = Domain.Fx.FxSettlementMath.ExactRate(foreign.Value, amount, dp)) is null)
                        issues.Add(new(r.Line, "Nguyên tệ", $"Không có tỷ giá 6 chữ số nào quy đổi {foreign} {currency} ra đúng {amount}."));
                }
                if (issues.Count == lineBefore && acc is not null)
                    lines.Add(new VoucherLineInput(acc.Id, debit > 0 ? Side.Debit : Side.Credit, amount, partner?.Id, currency, foreign, rate, Norm(r.LineDescription)));
            }
            if (issues.Count > before) continue;
            var d = lines.Where(l => l.Side == Side.Debit).Sum(l => l.Amount);
            var c = lines.Where(l => l.Side == Side.Credit).Sum(l => l.Amount);
            if (lines.Count < 2 || d != c) { issues.Add(new(first.Line, "Tổng", $"Chứng từ {g.Key}: Nợ {d} – Có {c}, phải có ít nhất 2 dòng và cân.")); continue; }
            groups.Add((g.Key, first.Line, date, description.Length > 500 ? description[..500] : description, type, lines));
        }
        var result = new ImportResult(false, groups.Count, 0, Sorted(issues), notes,
            TotalDebit: groups.Sum(g => g.Lines.Where(l => l.Side == Side.Debit).Sum(l => l.Amount)),
            TotalCredit: groups.Sum(g => g.Lines.Where(l => l.Side == Side.Credit).Sum(l => l.Amount)));
        if (!commit || issues.Count > 0 || groups.Count == 0) return result;
        var numbers = new List<string>();
        await db.InTransactionAsync(async () =>
        {
            foreach (var g in groups.OrderBy(g => g.Date).ThenBy(g => g.FirstLine))
            {
                var v = await WithLine(g.FirstLine, () => engine.CreateDraftAsync(ctx,
                    new VoucherInput(g.Type, g.Date, g.Date, g.Description, g.Lines, SourceSystem: ImportSource, SourceId: g.Key)));
                numbers.Add(v.Number);
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "import.vouchers", "company", ctx.CompanyId.ToString(), new { vouchers = groups.Count, lines = groups.Sum(g => g.Lines.Count) });
            await db.SaveChangesAsync();
        });
        return result with { Committed = true, Documents = numbers };
    }

    /// <summary>Submits every imported Draft the caller created (in date order).</summary>
    public async Task<ImportPostResult> SubmitImportedAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.VoucherSubmit);
        var ids = await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == ImportSource && v.Status == VoucherStatus.Draft && v.CreatedBy == ctx.UserId)
            .OrderBy(v => v.PostingDate).ThenBy(v => v.Number).Select(v => new { v.Id, v.Number }).ToListAsync();
        return await EachAsync(ids.Select(x => (x.Id, x.Number)), id => engine.SubmitAsync(ctx, id));
    }

    /// <summary>
    /// Approves and posts every submitted imported voucher in date order — each in its own transaction, the usual checks apply
    /// (approver ≠ preparer, open period, negative-balance policy); failures are listed and the rest continue.
    /// </summary>
    public async Task<ImportPostResult> ApproveAndPostImportedAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.VoucherApprove);
        ctx.Require(Permissions.VoucherPost);
        var ids = await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == ImportSource && v.Status == VoucherStatus.Submitted)
            .OrderBy(v => v.PostingDate).ThenBy(v => v.Number).Select(v => new { v.Id, v.Number }).ToListAsync();
        return await EachAsync(ids.Select(x => (x.Id, x.Number)), async id => { await engine.ApproveAsync(ctx, id); await engine.PostAsync(ctx, id); });
    }

    private async Task<ImportPostResult> EachAsync(IEnumerable<(Guid Id, string Number)> vouchers, Func<Guid, Task> act)
    {
        var done = 0;
        var failures = new List<string>();
        foreach (var (id, number) in vouchers)
        {
            try { await act(id); done++; }
            catch (DomainException ex) { failures.Add($"{number}: {ex.Message}"); db.ChangeTracker.Clear(); }
        }
        return new ImportPostResult(done, failures);
    }
}
