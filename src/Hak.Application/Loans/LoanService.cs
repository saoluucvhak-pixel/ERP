using System.Text.Json;
using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Fx;
using Hak.Domain.Loans;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Loans;

public sealed record LoanInput(string Code, string Description, Guid LenderId, Guid PrincipalAccountId, Guid InterestExpenseAccountId,
    Guid AccruedInterestAccountId, decimal ContractAmount, DateOnly StartDate, DateOnly MaturityDate, decimal AnnualRatePercent,
    DayCountBasis DayCount = DayCountBasis.Actual365, string? Currency = null, DateOnly? InterestFrom = null);

public sealed record LoanRegisterRow(Guid Id, string Code, string Description, string LenderCode, string AccountCode, string? Currency, LoanStatus Status,
    decimal ContractAmount, decimal Drawn, decimal Repaid, decimal Outstanding, decimal? OutstandingForeign, decimal? CurrentRatePercent,
    DateOnly MaturityDate, bool DueWithin12Months, decimal InterestAccrued);

public sealed record LoanCandidate(Guid JournalLineId, string VoucherNumber, DateOnly PostingDate, string Side, decimal Amount, decimal? ForeignAmount, string? Description);

public sealed record LoanReconcileRow(Guid AccountId, string AccountCode, Guid LenderId, string LenderCode, decimal LedgerBalance, decimal RegisterBalance,
    decimal RevaluationAdjustments, decimal Difference);

/// <summary>
/// Loans (master prompt: LOAN). Drawdowns and repayments are posted vouchers linked to the contract, never booked twice. The
/// principal account must not be an open-item account (loans are tracked per contract here). Interest is accrued by a
/// company-wide run per period, in period order: daily principal × rate of the day ÷ basis, rounded once per period. Foreign
/// loans accrue interest in the loan currency, converted at the rate entered for the run; the accrued interest line carries
/// the foreign amount so it is revalued and paid like any foreign item.
/// </summary>
public sealed class LoanService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string LoanSource = "HAK.LOAN";

    // ------------------------------------------------------------------ contracts

    public async Task<LoanContract> CreateAsync(SessionContext ctx, LoanInput input)
    {
        ctx.Require(Permissions.LoanManage);
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Description))
            throw new DomainException(ErrorCodes.ValidationFailed, "Số khế ước và nội dung bắt buộc.");
        if (input.ContractAmount <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Hạn mức / số tiền vay phải lớn hơn 0.");
        if (input.MaturityDate < input.StartDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày đáo hạn không được trước ngày bắt đầu.");
        var interestFrom = input.InterestFrom ?? input.StartDate;
        if (interestFrom < input.StartDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày bắt đầu tính lãi không được trước ngày bắt đầu khế ước.");
        if (input.AnnualRatePercent is < 0 or > 100) throw new DomainException(ErrorCodes.ValidationFailed, "Lãi suất năm phải từ 0 đến 100%.");
        return await db.InTransactionAsync(async () =>
        {
            var code = input.Code.Trim();
            if (await db.LoanContracts.AnyAsync(l => l.CompanyId == ctx.CompanyId && l.Code == code))
                throw new DomainException(ErrorCodes.DuplicateCode, $"Khế ước {code} đã tồn tại.");
            if (!await db.BusinessPartners.AnyAsync(p => p.Id == input.LenderId && p.CompanyId == ctx.CompanyId))
                throw new DomainException(ErrorCodes.PartnerWrongCompany, "Bên cho vay không tồn tại.");
            var principal = await PostableAccountAsync(ctx, input.PrincipalAccountId);
            if (principal.OpenItemNature != OpenItemNature.None || principal.CashKind != CashKind.None)
                throw new DomainException(ErrorCodes.ValidationFailed, $"TK {principal.Code} không dùng làm TK gốc vay (không phải tiền, không theo dõi công nợ theo chứng từ — khoản vay được theo dõi theo khế ước).");
            await PostableAccountAsync(ctx, input.InterestExpenseAccountId);
            await PostableAccountAsync(ctx, input.AccruedInterestAccountId);
            var functional = await db.Companies.Where(c => c.Id == ctx.CompanyId).Select(c => c.FunctionalCurrency).FirstAsync();
            var currency = string.IsNullOrWhiteSpace(input.Currency) || input.Currency.Trim().ToUpperInvariant() == functional ? null : input.Currency.Trim().ToUpperInvariant();
            if (currency is not null && !await db.Currencies.AnyAsync(c => c.Code == currency))
                throw new DomainException(ErrorCodes.ValidationFailed, $"Loại tiền {currency} chưa có trong danh mục.");

            var loan = new LoanContract
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Code = code, Description = input.Description.Trim(), LenderId = input.LenderId,
                PrincipalAccountId = principal.Id, InterestExpenseAccountId = input.InterestExpenseAccountId, AccruedInterestAccountId = input.AccruedInterestAccountId,
                Currency = currency, ContractAmount = input.ContractAmount, StartDate = input.StartDate, MaturityDate = input.MaturityDate,
                InterestFrom = interestFrom, DayCount = input.DayCount, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
            };
            loan.Rates.Add(new LoanRate
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, LoanId = loan.Id, EffectiveFrom = input.StartDate, AnnualRatePercent = input.AnnualRatePercent,
                Note = "Lãi suất ban đầu theo khế ước", CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            });
            db.LoanContracts.Add(loan);
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.create", "loan", loan.Id.ToString(), input, loan.RowVersion);
            await db.SaveChangesAsync();
            return loan;
        });
    }

    public async Task<LoanContract> ActivateAsync(SessionContext ctx, Guid loanId)
    {
        ctx.Require(Permissions.LoanManage);
        return await db.InTransactionAsync(async () =>
        {
            var loan = await LoadAsync(ctx, loanId, tracking: true);
            if (loan.Status != LoanStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Khế ước đang ở trạng thái {loan.Status}.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && loan.CreatedBy == ctx.UserId)
                throw new DomainException(ErrorCodes.SodViolation, "Người duyệt khế ước phải khác người lập.");
            var last = await LastActiveRunAsync(ctx.CompanyId);
            if (last is not null && loan.InterestFrom <= last.PeriodEnd)
                throw new DomainException(ErrorCodes.PeriodAccrued,
                    $"Đã trích lãi đến {last.PeriodEnd:dd/MM/yyyy}; ngày bắt đầu tính lãi phải sau ngày đó, hoặc hủy các lần trích lãi từ kỳ của khế ước.");
            loan.Status = LoanStatus.Active;
            loan.ActivatedAt = clock.UtcNow;
            loan.ActivatedBy = ctx.UserId;
            loan.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.activate", "loan", loan.Id.ToString(), null, loan.RowVersion);
            await db.SaveChangesAsync();
            return loan;
        });
    }

    public async Task CancelDraftAsync(SessionContext ctx, Guid loanId, string reason)
    {
        ctx.Require(Permissions.LoanManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy khế ước.");
        await db.InTransactionAsync(async () =>
        {
            var loan = await LoadAsync(ctx, loanId, tracking: true);
            if (loan.Status != LoanStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ hủy được khế ước nháp.");
            loan.Status = LoanStatus.Cancelled;
            loan.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.cancel", "loan", loan.Id.ToString(), new { reason }, loan.RowVersion);
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Rate change from a date (floating rate, renegotiation). Only after the last accrued period, so booked interest never changes.</summary>
    public async Task<LoanRate> AddRateAsync(SessionContext ctx, Guid loanId, DateOnly effectiveFrom, decimal annualRatePercent, string note)
    {
        ctx.Require(Permissions.LoanManage);
        if (string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi căn cứ thay đổi lãi suất (phụ lục, thông báo của bên cho vay…).");
        if (annualRatePercent is < 0 or > 100) throw new DomainException(ErrorCodes.ValidationFailed, "Lãi suất năm phải từ 0 đến 100%.");
        return await db.InTransactionAsync(async () =>
        {
            var loan = await LoadAsync(ctx, loanId, tracking: false);
            if (loan.Status is not (LoanStatus.Draft or LoanStatus.Active)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Khế ước đang ở trạng thái {loan.Status}.");
            if (effectiveFrom < loan.StartDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày hiệu lực không được trước ngày bắt đầu khế ước.");
            if (loan.Rates.Any(r => r.EffectiveFrom == effectiveFrom)) throw new DomainException(ErrorCodes.DuplicateCode, "Đã có lãi suất hiệu lực từ ngày này.");
            var accruedTo = await AccruedToAsync(loan.Id);
            if (accruedTo is { } end && effectiveFrom <= end)
                throw new DomainException(ErrorCodes.PeriodAccrued, $"Đã trích lãi khế ước này đến {end:dd/MM/yyyy}; lãi suất mới phải hiệu lực sau ngày đó (hoặc hủy các lần trích lãi trước).");
            var rate = new LoanRate
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, LoanId = loan.Id, EffectiveFrom = effectiveFrom, AnnualRatePercent = annualRatePercent,
                Note = note.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.LoanRates.Add(rate);
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.rate", "loan", loan.Id.ToString(), new { effectiveFrom, annualRatePercent, note });
            await db.SaveChangesAsync();
            return rate;
        });
    }

    /// <summary>Links posted drawdowns (credits) / repayments (debits) on the principal account for the lender to the contract.</summary>
    public async Task LinkAsync(SessionContext ctx, Guid loanId, IReadOnlyList<Guid> journalLineIds)
    {
        ctx.Require(Permissions.LoanManage);
        if (journalLineIds.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Chọn ít nhất một bút toán giải ngân / trả nợ.");
        await db.InTransactionAsync(async () =>
        {
            var loan = await LoadAsync(ctx, loanId, tracking: false);
            if (loan.Status != LoanStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ gắn giải ngân / trả nợ cho khế ước đang hiệu lực.");
            var lines = await (from l in db.JournalLines
                               join e in db.JournalEntries on l.JournalEntryId equals e.Id
                               join v in db.Vouchers on e.VoucherId equals v.Id
                               where journalLineIds.Contains(l.Id) && l.CompanyId == ctx.CompanyId
                               select new { l.Id, l.AccountId, l.PartnerId, l.ForeignCurrency, l.ForeignAmount, l.PostingDate, v.Status, v.Number }).AsNoTracking().ToListAsync();
            if (lines.Count != journalLineIds.Distinct().Count()) throw new DomainException(ErrorCodes.NotFound, "Có bút toán không tồn tại.");
            var accruedTo = await AccruedToAsync(loan.Id) ?? (await LastActiveRunAsync(ctx.CompanyId))?.PeriodEnd;
            foreach (var l in lines)
            {
                if (l.Status != VoucherStatus.Posted)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Chứng từ {l.Number} không còn hiệu lực (đã đảo).");
                if (l.AccountId != loan.PrincipalAccountId || l.PartnerId != loan.LenderId)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng của chứng từ {l.Number} không phải TK gốc vay của bên cho vay trong khế ước.");
                if (l.ForeignCurrency != loan.Currency || (loan.Currency is not null && l.ForeignAmount is null))
                    throw new DomainException(ErrorCodes.CurrencyMismatch, $"Dòng của chứng từ {l.Number} không cùng loại tiền với khế ước.");
                if (l.PostingDate < loan.StartDate)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Chứng từ {l.Number} trước ngày bắt đầu khế ước.");
                if (accruedTo is { } end && l.PostingDate >= loan.InterestFrom && l.PostingDate <= end)
                    throw new DomainException(ErrorCodes.PeriodAccrued, $"Chứng từ {l.Number} thuộc kỳ đã trích lãi (đến {end:dd/MM/yyyy}); hủy lần trích lãi đó trước để tính lại lãi.");
            }
            if (await db.LoanLinks.AnyAsync(k => journalLineIds.Contains(k.JournalLineId)))
                throw new DomainException(ErrorCodes.DuplicateSource, "Có bút toán đã gắn vào khế ước khác.");
            foreach (var id in journalLineIds.Distinct())
                db.LoanLinks.Add(new LoanLink { Id = Ids.New(), CompanyId = ctx.CompanyId, LoanId = loan.Id, JournalLineId = id, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId });
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.link", "loan", loan.Id.ToString(), new { journalLineIds });
            await db.SaveChangesAsync();
        });
    }

    public async Task<LoanContract> CloseAsync(SessionContext ctx, Guid loanId)
    {
        ctx.Require(Permissions.LoanManage);
        return await db.InTransactionAsync(async () =>
        {
            var loan = await LoadAsync(ctx, loanId, tracking: true);
            if (loan.Status != LoanStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Khế ước đang ở trạng thái {loan.Status}.");
            var moves = await MovesAsync([loan.Id], DateOnly.MaxValue);
            var outstanding = moves.Where(m => m.LoanId == loan.Id).Sum(m => loan.Currency is null ? m.Amount : m.Foreign ?? 0);
            if (outstanding != 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Khế ước còn dư nợ gốc {outstanding}; không tất toán được.");
            // Bắt buộc trích lãi đến kỳ chứa ngày trả gốc cuối trước khi tất toán (chị chốt 21/10/2026): đóng sớm sẽ mất lãi các ngày cuối.
            var lastMove = moves.Where(m => m.LoanId == loan.Id).Select(m => (DateOnly?)m.Date).Max();
            if (lastMove is { } lastRepayment)
            {
                var accruedTo = await AccruedToAsync(loan.Id) ?? (await LastActiveRunAsync(ctx.CompanyId))?.PeriodEnd;
                if (accruedTo is null || accruedTo < lastRepayment)
                    throw new DomainException(ErrorCodes.InterestPending,
                        $"Chưa trích lãi đến kỳ chứa ngày trả gốc cuối {lastRepayment:dd/MM/yyyy} (đã trích đến {(accruedTo is { } d ? d.ToString("dd/MM/yyyy") : "chưa trích")}); trích lãi kỳ đó rồi mới tất toán khế ước.");
            }
            loan.Status = LoanStatus.Closed;
            loan.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.close", "loan", loan.Id.ToString(), null, loan.RowVersion);
            await db.SaveChangesAsync();
            return loan;
        });
    }

    public async Task<IReadOnlyList<LoanCandidate>> CandidatesAsync(SessionContext ctx, Guid loanId)
    {
        ctx.Require(Permissions.ReportView);
        var loan = await LoadAsync(ctx, loanId, tracking: false);
        var linked = db.LoanLinks.Select(k => k.JournalLineId);
        var rows = await (from l in db.JournalLines
                          join e in db.JournalEntries on l.JournalEntryId equals e.Id
                          join v in db.Vouchers on e.VoucherId equals v.Id
                          where l.CompanyId == ctx.CompanyId && l.AccountId == loan.PrincipalAccountId && l.PartnerId == loan.LenderId
                                && v.Status == VoucherStatus.Posted && l.ForeignCurrency == loan.Currency && !linked.Contains(l.Id) && l.PostingDate >= loan.StartDate
                          orderby l.PostingDate, e.VoucherNumber
                          select new { l.Id, e.VoucherNumber, l.PostingDate, l.Side, l.Amount, l.ForeignAmount, l.Description }).AsNoTracking().ToListAsync();
        return rows.Where(r => loan.Currency is null || r.ForeignAmount is not null)
            .Select(r => new LoanCandidate(r.Id, r.VoucherNumber, r.PostingDate, r.Side == Side.Credit ? "Giải ngân" : "Trả gốc", r.Amount, r.ForeignAmount, r.Description)).ToList();
    }

    // ------------------------------------------------------------------ interest runs

    public async Task<LoanInterestRun> AccrueAsync(SessionContext ctx, Guid periodId, IReadOnlyList<FxRate>? rates = null)
    {
        ctx.Require(Permissions.VoucherPost);
        rates ??= [];
        return await db.InTransactionAsync(async () =>
        {
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == ctx.CompanyId)
                         ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại.");
            if (period.Status == PeriodStatus.Locked) throw new DomainException(ErrorCodes.PeriodLocked, $"Kỳ {period.StartDate:MM/yyyy} đã khóa sổ.");
            var last = await LastActiveRunAsync(ctx.CompanyId);
            if (last is not null && last.PeriodStart >= period.StartDate)
                throw new DomainException(ErrorCodes.PeriodAccrued, $"Đã trích lãi đến {last.PeriodEnd:dd/MM/yyyy}; hủy từ kỳ gần nhất nếu cần trích lại.");
            if (last is not null && last.PeriodEnd.AddDays(1) != period.StartDate)
                throw new DomainException(ErrorCodes.InterestPending, $"Chưa trích lãi kỳ trước (đã trích đến {last.PeriodEnd:dd/MM/yyyy}); trích theo thứ tự kỳ.");

            var loans = await db.LoanContracts.AsNoTracking().Include(l => l.Rates)
                .Where(l => l.CompanyId == ctx.CompanyId && l.Status == LoanStatus.Active && l.InterestFrom <= period.EndDate).OrderBy(l => l.Code).ToListAsync();
            var moves = await MovesAsync(loans.Select(l => l.Id).ToList(), period.EndDate);
            if (last is null && loans.FirstOrDefault(l => l.InterestFrom < period.StartDate && moves.Any(m => m.LoanId == l.Id && m.Date < period.StartDate)) is { } early)
                throw new DomainException(ErrorCodes.InterestPending,
                    $"Khế ước {early.Code} có dư nợ tính lãi từ {early.InterestFrom:dd/MM/yyyy}; lần trích lãi đầu tiên phải từ kỳ chứa ngày đó.");

            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            var decimals = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.DecimalPlaces);
            var funcDp = decimals[company.FunctionalCurrency];
            var byCurrency = rates.ToDictionary(r => r.Currency.ToUpperInvariant());
            var run = new LoanInterestRun
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, PeriodId = period.Id, PeriodStart = period.StartDate, PeriodEnd = period.EndDate,
                RatesJson = JsonSerializer.Serialize(rates), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.LoanInterestRuns.Add(run);
            var gl = new List<VoucherLineInput>();
            var result = new List<object>();
            foreach (var loan in loans)
            {
                var from = loan.InterestFrom > period.StartDate ? loan.InterestFrom : period.StartDate;
                var own = moves.Where(m => m.LoanId == loan.Id).Select(m => (m.Date, loan.Currency is null ? m.Amount : m.Foreign ?? 0)).ToList();
                var interest = InterestCalculator.Accrue(own, loan.Rates.Select(r => (r.EffectiveFrom, r.AnnualRatePercent)).ToList(), from, period.EndDate, loan.DayCount,
                    loan.Currency is null ? funcDp : decimals[loan.Currency]);
                if (interest <= 0) continue;
                decimal amount = interest;
                decimal? foreign = null;
                var label = $"Lãi vay {loan.Code} {from:dd/MM}–{period.EndDate:dd/MM/yyyy}";
                if (loan.Currency is { } cur)
                {
                    if (!byCurrency.TryGetValue(cur, out var r)) throw new DomainException(ErrorCodes.ValidationFailed, $"Thiếu tỷ giá {cur} để quy đổi lãi vay khế ước {loan.Code}.");
                    foreign = interest;
                    amount = MoneyMath.Convert(interest, r.CreditBalanceRate, funcDp);
                    gl.Add(new VoucherLineInput(loan.InterestExpenseAccountId, Side.Debit, amount, Description: label));
                    gl.Add(new VoucherLineInput(loan.AccruedInterestAccountId, Side.Credit, amount, loan.LenderId, cur, interest, r.CreditBalanceRate, label, loan.MaturityDate));
                }
                else
                {
                    gl.Add(new VoucherLineInput(loan.InterestExpenseAccountId, Side.Debit, amount, Description: label));
                    gl.Add(new VoucherLineInput(loan.AccruedInterestAccountId, Side.Credit, amount, loan.LenderId, Description: label, DueDate: loan.MaturityDate));
                }
                db.LoanInterestEntries.Add(new LoanInterestEntry
                {
                    Id = Ids.New(), CompanyId = ctx.CompanyId, LoanId = loan.Id, RunId = run.Id, PeriodStart = from, PeriodEnd = period.EndDate,
                    Amount = amount, ForeignAmount = foreign, CreatedAt = clock.UtcNow,
                });
                result.Add(new { loan = loan.Code, from, to = period.EndDate, interest, amount });
            }
            if (gl.Count > 0)
            {
                var v = await engine.PostSystemVoucherAsync(ctx, new VoucherInput(VoucherTypes.LoanInterest, period.EndDate, period.EndDate,
                    $"Trích lãi vay kỳ {period.StartDate:MM/yyyy}", gl, SourceSystem: LoanSource, SourceId: run.Id.ToString()));
                run.VoucherId = v.Id;
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.accrue", "loan_interest_run", run.Id.ToString(), new { period = period.StartDate, rates, result });
            await db.SaveChangesAsync();
            return run;
        });
    }

    public async Task ReverseRunAsync(SessionContext ctx, Guid runId, string reason)
    {
        ctx.Require(Permissions.VoucherReverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy lần trích lãi.");
        await db.InTransactionAsync(async () =>
        {
            var run = await db.LoanInterestRuns.FirstOrDefaultAsync(r => r.Id == runId && r.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Lần trích lãi không tồn tại.");
            if (run.Status != LoanRunStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, "Lần trích lãi đã bị hủy.");
            if (await db.LoanInterestRuns.AnyAsync(r => r.CompanyId == ctx.CompanyId && r.Status == LoanRunStatus.Active && r.PeriodStart > run.PeriodStart))
                throw new DomainException(ErrorCodes.PeriodAccrued, "Các kỳ sau đã trích lãi; hủy từ kỳ gần nhất trở về trước.");
            foreach (var e in await db.LoanInterestEntries.Where(e => e.RunId == run.Id && e.VoidedAt == null).ToListAsync())
                e.VoidedAt = clock.UtcNow;
            if (run.VoucherId is { } vid) await engine.ReverseCoreAsync(ctx, vid, run.PeriodEnd, $"Hủy trích lãi vay: {reason}", allowModuleOwned: true);
            run.Status = LoanRunStatus.Reversed;
            run.ReversedAt = clock.UtcNow;
            run.ReversedBy = ctx.UserId;
            await audit.AppendAsync(ctx.CompanyId, ctx, "loan.accrue_reverse", "loan_interest_run", run.Id.ToString(), new { reason });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<LoanInterestRun>> RunsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        return await db.LoanInterestRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId)
            .OrderByDescending(r => r.PeriodStart).ThenByDescending(r => r.CreatedAt).ToListAsync();
    }

    // ------------------------------------------------------------------ reports

    public async Task<IReadOnlyList<LoanRegisterRow>> RegisterAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var loans = await db.LoanContracts.AsNoTracking().Include(l => l.Rates)
            .Where(l => l.CompanyId == ctx.CompanyId && l.Status != LoanStatus.Cancelled).OrderBy(l => l.Code).ToListAsync();
        var moves = await MovesAsync(loans.Select(l => l.Id).ToList(), asOf);
        var entries = await db.LoanInterestEntries.AsNoTracking().Where(e => e.CompanyId == ctx.CompanyId && e.VoidedAt == null && e.PeriodEnd <= asOf).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Code);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Id, a => a.Code);
        return loans.Select(l =>
        {
            var own = moves.Where(m => m.LoanId == l.Id).ToList();
            var drawn = own.Where(m => m.Amount > 0).Sum(m => m.Amount);
            var repaid = -own.Where(m => m.Amount < 0).Sum(m => m.Amount);
            return new LoanRegisterRow(l.Id, l.Code, l.Description, partners[l.LenderId], accounts[l.PrincipalAccountId], l.Currency, l.Status, l.ContractAmount,
                drawn, repaid, drawn - repaid, l.Currency is null ? null : own.Sum(m => m.Foreign ?? 0),
                l.Rates.Where(r => r.EffectiveFrom <= asOf).OrderBy(r => r.EffectiveFrom).LastOrDefault()?.AnnualRatePercent,
                l.MaturityDate, l.Status == LoanStatus.Active && l.MaturityDate <= asOf.AddMonths(12), entries.Where(e => e.LoanId == l.Id).Sum(e => e.Amount));
        }).ToList();
    }

    /// <summary>
    /// Principal account balance per lender (credit positive) against the contracts' linked lines. Revaluation lines of foreign
    /// loans are shown separately; any other difference is a posting on the loan account not linked to a contract.
    /// </summary>
    public async Task<IReadOnlyList<LoanReconcileRow>> ReconcileAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var loans = await db.LoanContracts.AsNoTracking().Where(l => l.CompanyId == ctx.CompanyId && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Closed)).ToListAsync();
        var moves = await MovesAsync(loans.Select(l => l.Id).ToList(), asOf);
        var groups = loans.Select(l => (l.PrincipalAccountId, l.LenderId)).Distinct().ToList();
        var accountIds = groups.Select(g => g.PrincipalAccountId).Distinct().ToList();
        var lines = await db.JournalLines.AsNoTracking()
            .Where(l => l.CompanyId == ctx.CompanyId && accountIds.Contains(l.AccountId) && l.PostingDate <= asOf)
            .Select(l => new { l.AccountId, l.PartnerId, l.Side, l.Amount, l.ForeignCurrency, l.ForeignAmount }).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Code);
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code);
        return groups.Select(g =>
        {
            var own = lines.Where(l => l.AccountId == g.PrincipalAccountId && l.PartnerId == g.LenderId).ToList();
            var ledger = own.Sum(l => l.Side == Side.Credit ? l.Amount : -l.Amount);
            var revaluation = own.Where(l => l.ForeignCurrency != null && l.ForeignAmount == null).Sum(l => l.Side == Side.Credit ? l.Amount : -l.Amount);
            var register = moves.Where(m => loans.Any(l => l.Id == m.LoanId && l.PrincipalAccountId == g.PrincipalAccountId && l.LenderId == g.LenderId)).Sum(m => m.Amount);
            return new LoanReconcileRow(g.PrincipalAccountId, accounts[g.PrincipalAccountId], g.LenderId, partners[g.LenderId], ledger, register, revaluation, ledger - register - revaluation);
        }).OrderBy(r => r.AccountCode, StringComparer.Ordinal).ThenBy(r => r.LenderCode, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Principal movements of linked lines on posted (not reversed) vouchers: + drawdown (credit), − repayment (debit).</summary>
    private async Task<List<(Guid LoanId, DateOnly Date, decimal Amount, decimal? Foreign)>> MovesAsync(List<Guid> loanIds, DateOnly asOf)
    {
        var rows = await (from k in db.LoanLinks
                          join l in db.JournalLines on k.JournalLineId equals l.Id
                          join e in db.JournalEntries on l.JournalEntryId equals e.Id
                          join v in db.Vouchers on e.VoucherId equals v.Id
                          where loanIds.Contains(k.LoanId) && v.Status == VoucherStatus.Posted && l.PostingDate <= asOf
                          select new { k.LoanId, l.PostingDate, l.Side, l.Amount, l.ForeignAmount }).AsNoTracking().ToListAsync();
        return rows.Select(r => (r.LoanId, r.PostingDate, r.Side == Side.Credit ? r.Amount : -r.Amount,
            r.ForeignAmount is { } f ? (decimal?)(r.Side == Side.Credit ? f : -f) : null)).ToList();
    }

    private async Task<DateOnly?> AccruedToAsync(Guid loanId)
        => await db.LoanInterestEntries.AsNoTracking().Where(e => e.LoanId == loanId && e.VoidedAt == null)
            .OrderByDescending(e => e.PeriodEnd).Select(e => (DateOnly?)e.PeriodEnd).FirstOrDefaultAsync();

    private Task<LoanInterestRun?> LastActiveRunAsync(Guid companyId)
        => db.LoanInterestRuns.AsNoTracking().Where(r => r.CompanyId == companyId && r.Status == LoanRunStatus.Active)
            .OrderByDescending(r => r.PeriodStart).FirstOrDefaultAsync();

    private async Task<LoanContract> LoadAsync(SessionContext ctx, Guid id, bool tracking)
    {
        var q = db.LoanContracts.Include(l => l.Rates).Where(l => l.Id == id && l.CompanyId == ctx.CompanyId);
        if (!tracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync() ?? throw new DomainException(ErrorCodes.NotFound, "Khế ước vay không tồn tại.");
    }

    private async Task<Account> PostableAccountAsync(SessionContext ctx, Guid id)
    {
        var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
                  ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản không tồn tại.");
        if (acc.CompanyId != ctx.CompanyId) throw new DomainException(ErrorCodes.AccountWrongCompany, $"Tài khoản {acc.Code} không thuộc công ty.");
        if (!acc.IsPostable) throw new DomainException(ErrorCodes.AccountNotPostable, $"Tài khoản {acc.Code} không phải tài khoản chi tiết.");
        if (!acc.IsActive) throw new DomainException(ErrorCodes.AccountInactive, $"Tài khoản {acc.Code} đã ngừng sử dụng.");
        return acc;
    }
}
