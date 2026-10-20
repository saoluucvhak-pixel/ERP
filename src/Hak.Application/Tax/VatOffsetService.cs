using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Tax;

public sealed record VatOffsetLine(Guid AccountId, string AccountCode, Side Side, decimal Amount);

/// <summary>
/// Period-end VAT offset preview. Balances are cumulative up to the period end (so input VAT carried forward from earlier
/// periods counts): InputBalance = debit balance of the accounts under role VAT_INPUT, OutputBalance = credit balance of those
/// under VAT_OUTPUT. Offset = min(input, output); NetPayable = output − offset (to pay), CarryForward = input − offset (still deductible).
/// </summary>
public sealed record VatOffsetPreview(Guid PeriodId, DateOnly PeriodStart, DateOnly PeriodEnd, decimal InputBalance, decimal OutputBalance,
    decimal OffsetAmount, decimal NetPayable, decimal CarryForward, IReadOnlyList<VatOffsetLine> Lines, string Note,
    IReadOnlyList<VatOffsetAccount> InputAccounts, IReadOnlyList<VatOffsetAccount> OutputAccounts);

/// <summary>Tài khoản tham gia khấu trừ (mặc định theo vai trò VAT_INPUT / VAT_OUTPUT, hoặc do người dùng chọn).</summary>
public sealed record VatOffsetAccount(Guid AccountId, string Code, string Name);

/// <summary>
/// Kết chuyển / khấu trừ thuế GTGT cuối kỳ: Nợ thuế GTGT đầu ra / Có thuế GTGT đầu vào được khấu trừ, số tiền = phần nhỏ hơn của hai số dư.
/// Tạo chứng từ NHÁP (người có quyền duyệt và ghi sổ như mọi chứng từ). Mỗi kỳ chỉ một chứng từ khấu trừ còn hiệu lực. Tài khoản lấy theo vai
/// trò VAT_INPUT / VAT_OUTPUT và các tài khoản con của chúng (ví dụ 1331, 1332). Cách hạch toán là mặc định phổ biến; công ty dùng cách khác
/// ([CẦN XÁC MINH căn cứ TT99]) thì tạo chứng từ tay hoặc bộ quy tắc kết chuyển riêng.
/// </summary>
public sealed class VatOffsetService(HakDbContext db, AccountingEngine engine, AuditService audit)
{
    public const string SourceSystem = "HAK.VAT.OFFSET";
    public const string InputRole = "VAT_INPUT";
    public const string OutputRole = "VAT_OUTPUT";

    /// <param name="inputAccountIds">Tài khoản thuế đầu vào được khấu trừ do người dùng chọn; null = theo vai trò VAT_INPUT (cùng tài khoản con).</param>
    /// <param name="outputAccountIds">Tài khoản thuế đầu ra do người dùng chọn; null = theo vai trò VAT_OUTPUT (cùng tài khoản con).</param>
    public async Task<VatOffsetPreview> PreviewAsync(SessionContext ctx, Guid periodId, IReadOnlyList<Guid>? inputAccountIds = null, IReadOnlyList<Guid>? outputAccountIds = null)
    {
        ctx.Require(Permissions.ReportView);
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == ctx.CompanyId)
                     ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại.");
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToListAsync();
        List<Domain.Catalog.Account> Chosen(IReadOnlyList<Guid> chosen, string what)
        {
            var list = chosen.Distinct().Select(id => accounts.FirstOrDefault(a => a.Id == id)
                ?? throw new DomainException(ErrorCodes.NotFound, $"Tài khoản {what} đã chọn không tồn tại.")).ToList();
            var bad = list.FirstOrDefault(a => !a.IsPostable);
            if (bad is not null) throw new DomainException(ErrorCodes.ValidationFailed, $"Tài khoản {bad.Code} không hạch toán trực tiếp được; chọn tài khoản chi tiết.");
            return list;
        }
        List<Domain.Catalog.Account> FromRole(string role, string label)
        {
            var root = db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role).Select(m => (Guid?)m.AccountId).FirstOrDefault();
            return root is { } r ? Subtree(accounts, r)
                : throw new DomainException(ErrorCodes.RuleMissing, $"Chưa gán vai trò tài khoản {role} ({label}) ở Thiết lập; hoặc chọn tài khoản khấu trừ trực tiếp.");
        }
        var inputAccounts = inputAccountIds is { Count: > 0 } ? Chosen(inputAccountIds, "thuế đầu vào") : FromRole(InputRole, "thuế GTGT được khấu trừ");
        var outputAccounts = outputAccountIds is { Count: > 0 } ? Chosen(outputAccountIds, "thuế đầu ra") : FromRole(OutputRole, "thuế GTGT đầu ra");
        if (inputAccounts.Select(a => a.Id).Intersect(outputAccounts.Select(a => a.Id)).Any())
            throw new DomainException(ErrorCodes.ValidationFailed, "Một tài khoản không thể vừa là thuế đầu vào vừa là thuế đầu ra.");
        var ids = inputAccounts.Concat(outputAccounts).Select(a => a.Id).ToList();
        var sums = await db.JournalLines.AsNoTracking().Where(l => l.CompanyId == ctx.CompanyId && ids.Contains(l.AccountId) && l.PostingDate <= period.EndDate)
            .GroupBy(l => new { l.AccountId, l.Side }).Select(g => new { g.Key.AccountId, g.Key.Side, T = g.Sum(x => x.Amount) }).ToListAsync();
        decimal Net(Guid accountId, Side positive)
            => sums.Where(s => s.AccountId == accountId && s.Side == positive).Sum(s => s.T) - sums.Where(s => s.AccountId == accountId && s.Side != positive).Sum(s => s.T);

        var inputs = inputAccounts.Select(a => (Account: a, Balance: Net(a.Id, Side.Debit))).Where(x => x.Balance > 0).ToList();
        var outputs = outputAccounts.Select(a => (Account: a, Balance: Net(a.Id, Side.Credit))).Where(x => x.Balance > 0).ToList();
        var input = inputs.Sum(x => x.Balance);
        var output = outputs.Sum(x => x.Balance);
        var offset = Math.Min(input, output);

        var lines = new List<VatOffsetLine>();
        void Allocate(IEnumerable<(Domain.Catalog.Account Account, decimal Balance)> side, Side lineSide)
        {
            var left = offset;
            foreach (var (account, balance) in side.OrderBy(x => x.Account.Code, StringComparer.Ordinal))
            {
                if (left <= 0) break;
                var take = Math.Min(balance, left);
                lines.Add(new VatOffsetLine(account.Id, account.Code, lineSide, take));
                left -= take;
            }
        }
        if (offset > 0) { Allocate(outputs, Side.Debit); Allocate(inputs, Side.Credit); }
        return new VatOffsetPreview(period.Id, period.StartDate, period.EndDate, input, output, offset, output - offset, input - offset, lines,
            "Số dư lũy kế đến hết kỳ (gồm thuế đầu vào chuyển từ kỳ trước). Chưa tính điều chỉnh khác và không phải tờ khai chính thức.",
            inputAccounts.OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => new VatOffsetAccount(a.Id, a.Code, a.Name)).ToList(),
            outputAccounts.OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => new VatOffsetAccount(a.Id, a.Code, a.Name)).ToList());
    }

    public async Task<Voucher> GenerateAsync(SessionContext ctx, Guid periodId, IReadOnlyList<Guid>? inputAccountIds = null, IReadOnlyList<Guid>? outputAccountIds = null)
    {
        ctx.Require(Permissions.VoucherCreate);
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == ctx.CompanyId)
                     ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại.");
        var existing = await db.Vouchers.AsNoTracking()
            .FirstOrDefaultAsync(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == SourceSystem && v.SourceId == periodId.ToString() && v.Status != VoucherStatus.Cancelled);
        if (existing is not null)
            throw new DomainException(ErrorCodes.DuplicateSource,
                $"Kỳ đã có chứng từ khấu trừ thuế GTGT {existing.Number} ({existing.Status}). Hủy nháp hoặc đảo chứng từ đó trước khi tạo lại.");
        var pending = await db.Vouchers.CountAsync(v => v.CompanyId == ctx.CompanyId && v.PostingDate >= period.StartDate && v.PostingDate <= period.EndDate
                                                       && (v.Status == VoucherStatus.Submitted || v.Status == VoucherStatus.Approved));
        if (pending > 0)
            throw new DomainException(ErrorCodes.PeriodHasPending, $"Còn {pending} chứng từ đã gửi/đã duyệt chưa ghi sổ trong kỳ; xử lý trước khi khấu trừ thuế.");

        var preview = await PreviewAsync(ctx, periodId, inputAccountIds, outputAccountIds);
        if (preview.OffsetAmount <= 0)
            throw new DomainException(ErrorCodes.VoucherNoLines, "Không có số thuế GTGT để khấu trừ (thuế đầu vào hoặc đầu ra bằng 0).");
        var description = $"Khấu trừ thuế GTGT kỳ {period.StartDate:MM/yyyy}";
        var voucher = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, period.EndDate, period.EndDate, description,
            preview.Lines.Select(l => new VoucherLineInput(l.AccountId, l.Side, l.Amount, Description: description)).ToList(),
            SourceSystem: SourceSystem, SourceId: periodId.ToString(), SourceVersion: "1"));
        return voucher;
    }

    private static List<Domain.Catalog.Account> Subtree(List<Domain.Catalog.Account> all, Guid rootId)
    {
        var result = new List<Domain.Catalog.Account>();
        var queue = new Queue<Guid>([rootId]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var account = all.FirstOrDefault(a => a.Id == id);
            if (account is null) continue;
            if (account.IsPostable) result.Add(account);
            foreach (var child in all.Where(a => a.ParentId == id)) queue.Enqueue(child.Id);
        }
        return result;
    }
}
