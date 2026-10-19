using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Loans;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Loans;

public enum ScheduleStatus { Pending, Paid, Overdue }

public sealed record ScheduleRow(Guid Id, DateOnly DueDate, decimal PrincipalDue, decimal InterestDue, decimal PrincipalOpen, ScheduleStatus Status, string? Note);

public sealed record UpcomingRepayment(Guid LoanId, string LoanCode, DateOnly DueDate, decimal PrincipalOpen, decimal InterestDue, ScheduleStatus Status)
{
    public decimal Total => PrincipalOpen + InterestDue;
}

/// <summary>
/// Lịch trả nợ gốc và lãi của khế ước (tài liệu thiết kế chu trình tài chính, bảng debt_schedules): kế toán nhập hoặc sinh lịch trả gốc đều, sửa được (có nhật ký kiểm toán).
/// Không ghi Nhật ký. Trạng thái suy ra từ số gốc đã trả (các dòng nợ đã ghi sổ gắn với khế ước) phân bổ theo thứ tự ngày đến hạn: Đã trả, Chờ trả, Quá hạn.
/// Lãi kỳ được coi là đã trả cùng kỳ gốc; lãi thực tế vẫn do trích lãi hằng kỳ ghi sổ.
/// </summary>
public sealed class LoanScheduleService(HakDbContext db, AuditService audit, IClock clock)
{
    public async Task<IReadOnlyList<ScheduleDraftItem>> PreviewEqualPrincipalAsync(SessionContext ctx, Guid loanId, int installments, DateOnly firstDue, decimal? annualRatePercent)
    {
        ctx.Require(Permissions.ReportView);
        var loan = await LoadAsync(ctx, loanId);
        var rate = annualRatePercent ?? loan.Rates.Where(r => r.EffectiveFrom <= loan.StartDate).OrderBy(r => r.EffectiveFrom).LastOrDefault()?.AnnualRatePercent
                   ?? throw new DomainException(ErrorCodes.ValidationFailed, "Khế ước chưa có lãi suất; nhập lãi suất để sinh lịch.");
        return LoanScheduleCalculator.EqualPrincipal(loan.ContractAmount, installments, loan.StartDate, firstDue, rate, loan.DayCount, await DecimalsAsync(ctx.CompanyId, loan));
    }

    public async Task<IReadOnlyList<ScheduleRow>> SaveAsync(SessionContext ctx, Guid loanId, IReadOnlyList<ScheduleDraftItem> items)
    {
        ctx.Require(Permissions.LoanManage);
        if (items.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Lịch trả nợ chưa có kỳ nào.");
        if (items.Any(i => i.PrincipalDue < 0 || i.InterestDue < 0 || i.PrincipalDue + i.InterestDue == 0)) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi kỳ phải có gốc hoặc lãi, không âm.");
        if (items.Select(i => i.DueDate).Distinct().Count() != items.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi ngày đến hạn chỉ một kỳ.");
        return await db.InTransactionAsync(async () =>
        {
            var loan = await LoadAsync(ctx, loanId);
            if (loan.Status is LoanStatus.Cancelled or LoanStatus.Closed) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Khế ước {loan.Code} đã tất toán hoặc hủy.");
            var dp = await DecimalsAsync(ctx.CompanyId, loan);
            if (items.Any(i => !MoneyMath.HasAtMostDecimals(i.PrincipalDue, dp) || !MoneyMath.HasAtMostDecimals(i.InterestDue, dp)))
                throw new DomainException(ErrorCodes.AmountPrecision, $"Số tiền tối đa {dp} chữ số thập phân.");
            if (items.Sum(i => i.PrincipalDue) > loan.ContractAmount)
                throw new DomainException(ErrorCodes.ValidationFailed, $"Tổng nợ gốc theo lịch vượt số tiền khế ước {loan.ContractAmount:N0}.");
            db.LoanScheduleItems.RemoveRange(await db.LoanScheduleItems.Where(s => s.LoanId == loanId).ToListAsync());
            foreach (var i in items.OrderBy(i => i.DueDate))
                db.LoanScheduleItems.Add(new LoanScheduleItem { Id = Ids.New(), CompanyId = ctx.CompanyId, LoanId = loanId, DueDate = i.DueDate, PrincipalDue = i.PrincipalDue, InterestDue = i.InterestDue });
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.schedule_save", "loan", loanId.ToString(), new { loan.Code, periods = items.Count, principal = items.Sum(i => i.PrincipalDue), interest = items.Sum(i => i.InterestDue) });
            await db.SaveChangesAsync();
            return await ListCoreAsync(loan, DateOnly.FromDateTime(clock.UtcNow));
        });
    }

    public async Task<IReadOnlyList<ScheduleRow>> ListAsync(SessionContext ctx, Guid loanId, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        return await ListCoreAsync(await LoadAsync(ctx, loanId), asOf);
    }

    /// <summary>Các kỳ trả nợ chưa trả của khế ước VND đang hiệu lực (cho kế hoạch tiền): gốc còn phải trả và lãi kỳ, kể cả quá hạn.</summary>
    public async Task<IReadOnlyList<UpcomingRepayment>> UpcomingAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var loans = await db.LoanContracts.AsNoTracking().Include(l => l.Rates).Where(l => l.CompanyId == ctx.CompanyId && l.Status == LoanStatus.Active && l.Currency == null).ToListAsync();
        var result = new List<UpcomingRepayment>();
        foreach (var l in loans)
            foreach (var r in await ListCoreAsync(l, asOf))
                if (r.Status != ScheduleStatus.Paid) result.Add(new UpcomingRepayment(l.Id, l.Code, r.DueDate, r.PrincipalOpen, r.InterestDue, r.Status));
        return result.OrderBy(r => r.DueDate).ToList();
    }

    private async Task<IReadOnlyList<ScheduleRow>> ListCoreAsync(LoanContract loan, DateOnly asOf)
    {
        var items = await db.LoanScheduleItems.AsNoTracking().Where(s => s.LoanId == loan.Id).OrderBy(s => s.DueDate).ToListAsync();
        var repaid = await (from k in db.LoanLinks join l in db.JournalLines on k.JournalLineId equals l.Id
                            join e in db.JournalEntries on l.JournalEntryId equals e.Id join v in db.Vouchers on e.VoucherId equals v.Id
                            where k.LoanId == loan.Id && v.Status == VoucherStatus.Posted && l.Side == Side.Debit select l.Amount).ToListAsync();
        var left = repaid.Sum();   // gốc đã trả, phân bổ dần cho các kỳ theo thứ tự đến hạn
        var rows = new List<ScheduleRow>();
        foreach (var s in items)
        {
            var paid = Math.Min(left, s.PrincipalDue); left -= paid;
            var open = s.PrincipalDue - paid;
            // Kỳ chỉ trả lãi (gốc 0) chưa theo dõi được thanh toán lãi nên chỉ có Chờ trả / Quá hạn.
            var status = s.PrincipalDue > 0 && open == 0 ? ScheduleStatus.Paid : s.DueDate < asOf ? ScheduleStatus.Overdue : ScheduleStatus.Pending;
            rows.Add(new ScheduleRow(s.Id, s.DueDate, s.PrincipalDue, s.InterestDue, open, status, s.Note));
        }
        return rows;
    }

    private async Task<LoanContract> LoadAsync(SessionContext ctx, Guid loanId)
        => await db.LoanContracts.AsNoTracking().Include(l => l.Rates).FirstOrDefaultAsync(l => l.Id == loanId && l.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Khế ước không tồn tại.");

    private async Task<int> DecimalsAsync(Guid companyId, LoanContract loan)
    {
        var code = loan.Currency ?? (await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId)).FunctionalCurrency;
        return await db.Currencies.Where(c => c.Code == code).Select(c => c.DecimalPlaces).FirstAsync();
    }
}
