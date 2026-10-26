using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Domain.Common;
using Hak.Domain.Ledger;
using Hak.Domain.Settlement;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed partial class InvoiceService
{
    private sealed record ReplacementAllocation(Guid CounterLineId, decimal Amount, DateOnly Date);

    private async Task<List<ReplacementAllocation>> CaptureReplacementAllocationsAsync(SessionContext ctx, CommercialInvoice replacement)
    {
        if (replacement.ReplacesInvoiceId is not { } originalId) return [];
        var original = await db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == originalId && i.CompanyId == ctx.CompanyId);
        var lines = await (from l in db.JournalLines.AsNoTracking() join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                           where e.VoucherId == original.VoucherId select l).ToListAsync();
        var ids = lines.Select(l => l.Id).ToList();
        var allocations = await db.SettlementAllocations.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.VoidedAt == null
            && (ids.Contains(a.DebitLineId) || ids.Contains(a.CreditLineId))).OrderBy(a => a.AllocationDate).ThenBy(a => a.Id).ToListAsync();
        if (allocations.Count == 0) return [];
        if (original.PartnerId != replacement.PartnerId || original.Currency != replacement.Currency || original.ExchangeRate != replacement.ExchangeRate)
            throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn gốc có phân bổ: xử lý phân bổ trước khi đổi đối tượng/tiền tệ/tỷ giá.");
        if (allocations.Any(a => a.ForeignAmount is not null))
            throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn ngoại tệ đã phân bổ: xử lý phân bổ và chênh lệch tỷ giá trước khi thay thế; không tự chuyển số tiền phân bổ.");
        foreach (var date in allocations.Select(a => a.AllocationDate > replacement.PostingDate ? a.AllocationDate : replacement.PostingDate).Distinct())
        {
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == ctx.CompanyId && p.StartDate <= date && p.EndDate >= date);
            if (period is null || period.Status != Domain.Organization.PeriodStatus.Open)
                throw new DomainException(ErrorCodes.PeriodLocked, "Kỳ tái phân bổ hóa đơn thay thế không mở; xử lý phân bổ trước khi phát hành.");
        }
        return allocations.Where(a => ids.Contains(a.DebitLineId) != ids.Contains(a.CreditLineId))
            .Select(a => new ReplacementAllocation(ids.Contains(a.DebitLineId) ? a.CreditLineId : a.DebitLineId, a.Amount, a.AllocationDate)).ToList();
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
                var date = new[] { invoice.PostingDate, old.Date, counter.PostingDate }.Max();
                // Future allocations must not be moved into a locked period.
                var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == ctx.CompanyId && p.StartDate <= date && p.EndDate >= date);
                if (period is null || period.Status != Domain.Organization.PeriodStatus.Open)
                    throw new DomainException(ErrorCodes.PeriodLocked, "Kỳ tái phân bổ hóa đơn thay thế không mở.");
                var (debit, credit) = line.Side == Side.Debit ? (line, counter) : (counter, line);
                await settlement.AllocateCoreAsync(ctx.CompanyId, debit, credit, amount, date, "INVOICE_REPLACEMENT", invoice.VoucherId, ctx.UserId);
                left -= amount;
            }
            // Any excess stays as an unapplied customer credit/supplier advance; no cash voucher is recreated.
        }
        await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.replacement.reallocate", "invoice", invoice.Id.ToString(),
            new { invoice.ReplacesInvoiceId, invoice.VoucherId, priorCount = prior.Count });
    }
}
