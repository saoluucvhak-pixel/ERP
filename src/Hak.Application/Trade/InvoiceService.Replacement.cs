using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Domain.Common;
using Hak.Domain.Fx;
using Hak.Domain.Ledger;
using Hak.Domain.Settlement;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record ReplacementAllocationView(Guid OriginalAllocationId, Guid CounterLineId, string? Currency, decimal Amount, decimal? ForeignAmount, decimal UnappliedAmount, decimal? UnappliedForeign, DateOnly Date);

public sealed partial class InvoiceService
{
    public async Task<IReadOnlyList<ReplacementAllocationView>> ReplacementAllocationsAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.VoucherView);
        var invoice = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == ctx.CompanyId)
            ?? throw new DomainException(ErrorCodes.NotFound, "Không có hóa đơn thay thế.");
        if (invoice.Status != InvoiceStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, "Xem kế hoạch chuyển phân bổ trước khi ghi sổ hóa đơn thay thế.");
        var plan = await CaptureReplacementAllocationsAsync(ctx, invoice);
        var result = new List<ReplacementAllocationView>();
        // Tải theo lô (một truy vấn cho mọi phân bổ gốc, một cho mọi dòng đối ứng) thay vì hai truy vấn mỗi phần tử.
        var allocationIds = plan.Select(p => p.OriginalAllocationId).Distinct().ToList();
        var oldAllocations = (await db.SettlementAllocations.AsNoTracking().Where(a => allocationIds.Contains(a.Id)).ToListAsync()).ToDictionary(a => a.Id);
        var counterIds = plan.Select(p => p.CounterLineId).Distinct().ToList();
        var currencies = (await db.JournalLines.AsNoTracking().Where(l => counterIds.Contains(l.Id)).Select(l => new { l.Id, l.ForeignCurrency }).ToListAsync()).ToDictionary(l => l.Id, l => l.ForeignCurrency);
        foreach (var item in plan)
        {
            var old = oldAllocations[item.OriginalAllocationId];
            var currency = currencies[item.CounterLineId];
            result.Add(new(item.OriginalAllocationId, item.CounterLineId, currency, item.Amount, item.ForeignAmount, old.Amount - item.Amount,
                old.ForeignAmount is { } foreign ? foreign - (item.ForeignAmount ?? 0) : null, item.Date));
        }
        return result;
    }
    private sealed record ReplacementAllocation(Guid OriginalAllocationId, Guid CounterLineId, decimal Amount, decimal? ForeignAmount, DateOnly Date);

    private async Task<List<ReplacementAllocation>> CaptureReplacementAllocationsAsync(SessionContext ctx, CommercialInvoice replacement)
    {
        if (replacement.ReplacesInvoiceId is not { } originalId) return [];
        var original = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstAsync(i => i.Id == originalId && i.CompanyId == ctx.CompanyId);
        var lines = await (from l in db.JournalLines.AsNoTracking() join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                           where e.VoucherId == original.VoucherId select l).ToListAsync();
        var ids = lines.Select(l => l.Id).ToList();
        var allocations = await db.SettlementAllocations.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.VoidedAt == null
            && (ids.Contains(a.DebitLineId) || ids.Contains(a.CreditLineId))).OrderBy(a => a.AllocationDate).ThenBy(a => a.Id).ToListAsync();
        if (allocations.Count == 0) return [];
        if (original.PartnerId != replacement.PartnerId || original.Currency != replacement.Currency || original.ExchangeRate != replacement.ExchangeRate)
            throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn gốc có phân bổ: xử lý phân bổ trước khi đổi đối tượng/tiền tệ/tỷ giá.");
        if (allocations.Any(a => a.ForeignAmount is not null) && (original.PaymentDiscountPercent != replacement.PaymentDiscountPercent
            || original.PaymentDiscountDays != replacement.PaymentDiscountDays || original.PaymentDiscountOptions != replacement.PaymentDiscountOptions
            || (original.PaymentDiscountPercent != null || original.PaymentDiscountOptions != null) && original.InvoiceDate != replacement.InvoiceDate))
            throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn ngoại tệ đã thanh toán: giữ nguyên điều khoản chiết khấu thanh toán khi chuyển phân bổ.");
        // Một truy vấn kỳ kế toán của công ty rồi tra trong bộ nhớ (trước đây một truy vấn cho mỗi ngày phân bổ khác nhau).
        var periods = await db.AccountingPeriods.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToListAsync();
        foreach (var date in allocations.Select(a => a.AllocationDate > replacement.PostingDate ? a.AllocationDate : replacement.PostingDate).Distinct())
        {
            var period = periods.FirstOrDefault(p => p.StartDate <= date && p.EndDate >= date);
            if (period is null || period.Status != Domain.Organization.PeriodStatus.Open)
                throw new DomainException(ErrorCodes.PeriodLocked, "Kỳ tái phân bổ hóa đơn thay thế không mở; xử lý phân bổ trước khi phát hành.");
        }
        if (allocations.Any(a => ids.Contains(a.DebitLineId) == ids.Contains(a.CreditLineId)))
            throw new DomainException(ErrorCodes.ValidationFailed, "Phân bổ nội bộ hóa đơn không hỗ trợ tự chuyển khi thay thế.");
        var settlement = new SettlementService(db, audit, clock);
        var dp = await settlement.FunctionalDecimalsAsync(ctx.CompanyId);
        var role = replacement.Kind == InvoiceKind.Sales ? "AR_TRADE" : "AP_TRADE";
        var account = await db.AccountRoleMappings.Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role).Select(m => m.AccountId).SingleAsync();
        var side = replacement.Kind == InvoiceKind.Sales ? Side.Debit : Side.Credit;
        var newForeign = replacement.Currency == null ? 0 : replacement.Lines.Sum(l => l.Amount);
        var newBook = replacement.Lines.Sum(l => l.FunctionalAmount);
        var newVat = replacement.Lines.Sum(l => l.VatAmount);
        if (replacement.Currency == null) newBook += newVat;
        else if (newForeign > 0 && FxSettlementMath.ExactRate(newForeign, newBook, dp) is null)
            throw new DomainException(ErrorCodes.FxBookValue, "Giá trị hóa đơn thay thế không biểu diễn được bằng tỷ giá ghi sổ.");
        var counters = new Dictionary<Guid, (JournalLine Line, decimal Foreign, decimal Book)>();
        var groups = allocations.GroupBy(a => ids.Contains(a.DebitLineId) ? a.CreditLineId : a.DebitLineId).ToList();
        // Dòng đối ứng và chứng từ của chúng tải theo lô (hai truy vấn cho mọi nhóm).
        var groupKeys = groups.Select(g => g.Key).ToList();
        var counterLines = (await db.JournalLines.AsNoTracking().Where(l => groupKeys.Contains(l.Id) && l.CompanyId == ctx.CompanyId).ToListAsync()).ToDictionary(l => l.Id);
        var entryIds = counterLines.Values.Select(l => l.JournalEntryId).Distinct().ToList();
        var vouchersByEntry = (await (from e in db.JournalEntries.AsNoTracking() join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id where entryIds.Contains(e.Id) select new { EntryId = e.Id, Voucher = v }).ToListAsync())
            .ToDictionary(x => x.EntryId, x => x.Voucher);
        foreach (var group in groups)
        {
            var counter = counterLines.TryGetValue(group.Key, out var cl) ? cl : throw new InvalidOperationException("Dòng đối ứng của phân bổ không tồn tại.");
            var voucher = vouchersByEntry.TryGetValue(counter.JournalEntryId, out var cv) ? cv : throw new InvalidOperationException("Chứng từ của dòng đối ứng không tồn tại.");
            if (voucher.VoucherType == VoucherTypes.Revaluation)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Công nợ đã đánh giá lại: xử lý lần đánh giá lại trước khi thay thế, không chuyển sai giá trị ghi sổ.");
            if (voucher.SourceSystem == PaymentDiscountService.Source && (original.Lines.Sum(l => l.Amount) != replacement.Lines.Sum(l => l.Amount)
                || original.Lines.Sum(l => l.FunctionalAmount) != replacement.Lines.Sum(l => l.FunctionalAmount) || original.Lines.Sum(l => l.VatAmount) != replacement.Lines.Sum(l => l.VatAmount)))
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Khoản đã hưởng/cấp chiết khấu thanh toán: đổi trị giá hóa đơn cần đối chiếu lại quyền lợi chiết khấu trước khi thay thế; không tự chuyển khoản chiết khấu thừa thành tiền ứng trước.");
            if (counter.AccountId != account || counter.PartnerId != replacement.PartnerId || counter.Side == side)
                throw new DomainException(ErrorCodes.SettlementMismatch, "Tài khoản/đối tượng/chiều công nợ thay thế khác phân bổ gốc.");
            var open = await settlement.OpenBothAsync(counter);
            counters[group.Key] = (counter, open.Foreign + group.Sum(a => a.ForeignAmount ?? 0), open.Amount + group.Sum(a => a.Amount));
        }
        var plan = new List<ReplacementAllocation>();
        foreach (var a in allocations)
        {
            var counterId = ids.Contains(a.DebitLineId) ? a.CreditLineId : a.DebitLineId;
            var c = counters[counterId];
            decimal amount; decimal? foreign = null;
            if (c.Line.ForeignCurrency is { } currency)
            {
                if (currency != replacement.Currency || a.ForeignAmount is not > 0)
                    throw new DomainException(ErrorCodes.FxBookValue, "Phân bổ ngoại tệ thiếu nguyên tệ hoặc sai loại tiền; cần đối chiếu trước khi thay thế.");
                var f = Math.Min(a.ForeignAmount.Value, Math.Min(newForeign, c.Foreign));
                if (f <= 0) { plan.Add(new(a.Id, counterId, 0, 0, a.AllocationDate)); continue; }
                amount = FxSettlementMath.BookValue(a.ForeignAmount.Value, a.Amount, f, dp);
                if (amount <= 0 || FxSettlementMath.BookValue(newForeign, newBook, f, dp) != amount || FxSettlementMath.BookValue(c.Foreign, c.Book, f, dp) != amount)
                    throw new DomainException(ErrorCodes.FxBookValue, "Phần chuyển phân bổ không khớp giá trị ghi sổ hai phía/làm tròn; cần xử lý chênh lệch tỷ giá riêng trước khi phát hành.");
                foreign = f; newForeign -= f; newBook -= amount;
                counters[counterId] = (c.Line, c.Foreign - f, c.Book - amount);
            }
            else
            {
                var available = replacement.Currency == null ? newBook : newVat;
                amount = Math.Min(a.Amount, Math.Min(available, c.Book));
                if (replacement.Currency == null) newBook -= amount; else newVat -= amount;
                counters[counterId] = (c.Line, c.Foreign, c.Book - amount);
            }
            plan.Add(new(a.Id, counterId, amount, foreign, a.AllocationDate));
        }
        return plan;
    }

    private async Task ReallocateReplacementAsync(SessionContext ctx, CommercialInvoice invoice, IReadOnlyList<ReplacementAllocation> prior)
    {
        if (prior.Count == 0) return;
        var settlement = new SettlementService(db, audit, clock);
        var newLines = await (from l in db.JournalLines.AsNoTracking() join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                              where e.VoucherId == invoice.VoucherId && l.PartnerId == invoice.PartnerId select l).OrderBy(l => l.LineNo).ToListAsync();
        foreach (var old in prior)
        {
            var counter = await db.JournalLines.AsNoTracking().FirstAsync(l => l.Id == old.CounterLineId && l.CompanyId == ctx.CompanyId);
            var left = old.Amount;
            foreach (var line in newLines.Where(l => l.AccountId == counter.AccountId && l.Side != counter.Side && l.ForeignCurrency == counter.ForeignCurrency))
            {
                var amount = Math.Min(left, Math.Min(await settlement.OpenAmountAsync(line), await settlement.OpenAmountAsync(counter)));
                if (amount <= 0) continue;
                if (old.ForeignAmount is { } foreign)
                {
                    var d = await settlement.OpenBothAsync(line); var c = await settlement.OpenBothAsync(counter);
                    var dp = await settlement.FunctionalDecimalsAsync(ctx.CompanyId);
                    if (amount != old.Amount || foreign > d.Foreign || foreign > c.Foreign
                        || FxSettlementMath.BookValue(d.Foreign, d.Amount, foreign, dp) != amount || FxSettlementMath.BookValue(c.Foreign, c.Amount, foreign, dp) != amount)
                        throw new DomainException(ErrorCodes.FxBookValue, "Nguồn phân bổ đã đổi sau preflight; không chuyển một phần sai giá trị ghi sổ.");
                }
                var date = new[] { invoice.PostingDate, old.Date, counter.PostingDate }.Max();
                // Future allocations must not be moved into a locked period.
                var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == ctx.CompanyId && p.StartDate <= date && p.EndDate >= date);
                if (period is null || period.Status != Domain.Organization.PeriodStatus.Open)
                    throw new DomainException(ErrorCodes.PeriodLocked, "Kỳ tái phân bổ hóa đơn thay thế không mở.");
                var (debit, credit) = line.Side == Side.Debit ? (line, counter) : (counter, line);
                var allocation = await settlement.AllocateCoreAsync(ctx.CompanyId, debit, credit, amount, date, "INVOICE_REPLACEMENT", invoice.VoucherId, ctx.UserId, old.ForeignAmount);
                await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.replacement.allocation", "settlement_allocation", allocation.Id.ToString(), new { old.OriginalAllocationId, newAllocationId = allocation.Id, old.CounterLineId, amount, old.ForeignAmount });
                left -= amount;
            }
            // Any excess stays as an unapplied customer credit/supplier advance; no cash voucher is recreated.
        }
        await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.replacement.reallocate", "invoice", invoice.Id.ToString(),
            new { invoice.ReplacesInvoiceId, invoice.VoucherId, priorCount = prior.Count });
    }
}
