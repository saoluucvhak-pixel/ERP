using Hak.Application.Accounting;
using Hak.Domain.Common;
using Hak.Domain.Fx;
using Hak.Domain.Ledger;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed partial class InvoiceService
{
    /// <summary>
    /// Receivable/payable lines of an invoice. Functional-currency invoice: one line for the total. Foreign-currency invoice:
    /// the goods part as a foreign open item (foreign amount = Σ line amounts, rate reproducing Σ functional amounts exactly)
    /// and VAT — computed in the functional currency — as a separate functional line. When <paramref name="settleVoucherId"/> is
    /// given (credit notes), each line settles the matching line of that voucher if it is still open enough and, for the
    /// foreign part, if its carrying amount equals the credit; otherwise the credit stays an unallocated open item.
    /// </summary>
    private async Task<List<VoucherLineInput>> PartnerLinesAsync(Guid companyId, CommercialInvoice inv, Guid account, Side side, string label,
        DateOnly? dueDate, Guid? settleVoucherId, decimal? partnerNetOverride = null)
    {
        if (partnerNetOverride is not null && inv.Currency is not null) throw new DomainException(ErrorCodes.ValidationFailed, "Bù trừ chứng từ CKTM chưa hỗ trợ hóa đơn ngoại tệ.");
        var net = partnerNetOverride ?? inv.Lines.Sum(x => x.FunctionalAmount);
        var vat = inv.Lines.Sum(x => x.VatAmount);
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var dp = await db.Currencies.Where(c => c.Code == company.FunctionalCurrency).Select(c => c.DecimalPlaces).FirstAsync();
        var foreign = inv.Currency is { } cur && cur != company.FunctionalCurrency;
        var originals = settleVoucherId is null ? []
            : await (from jl in db.JournalLines join e in db.JournalEntries on jl.JournalEntryId equals e.Id
                     where e.VoucherId == settleVoucherId && jl.AccountId == account && jl.Side != side && jl.PartnerId == inv.PartnerId
                     select jl).AsNoTracking().ToListAsync();

        var result = new List<VoucherLineInput>();
        if (!foreign)
        {
            var target = originals.FirstOrDefault(o => o.ForeignCurrency is null);
            result.Add(new VoucherLineInput(account, side, net + vat, inv.PartnerId, Description: label, DueDate: dueDate,
                SettleJournalLineId: await SettleableAsync(target, net + vat, null, dp)));
            return result;
        }
        var foreignAmount = inv.Lines.Sum(x => x.Amount);
        if (net > 0)
        {
            var rate = FxSettlementMath.ExactRate(foreignAmount, net, dp)
                       ?? throw new DomainException(ErrorCodes.ValidationFailed,
                           $"Không biểu diễn được {net} = {foreignAmount} {inv.Currency} × tỷ giá 6 chữ số thập phân; tách hóa đơn thành nhiều hóa đơn nhỏ hơn.");
            var target = originals.FirstOrDefault(o => o.ForeignCurrency == inv.Currency);
            result.Add(new VoucherLineInput(account, side, net, inv.PartnerId, inv.Currency, foreignAmount, rate, label, dueDate,
                await SettleableAsync(target, net, foreignAmount, dp)));
        }
        if (vat > 0)
        {
            var target = originals.FirstOrDefault(o => o.ForeignCurrency is null);
            result.Add(new VoucherLineInput(account, side, vat, inv.PartnerId, Description: $"{label} – thuế GTGT", DueDate: dueDate,
                SettleJournalLineId: await SettleableAsync(target, vat, null, dp)));
        }
        return result;
    }

    private async Task<Guid?> SettleableAsync(JournalLine? target, decimal amount, decimal? foreignAmount, int dp)
    {
        if (target is null) return null;
        var allocs = await db.SettlementAllocations.AsNoTracking()
            .Where(a => (a.DebitLineId == target.Id || a.CreditLineId == target.Id) && a.VoidedAt == null)
            .Select(a => new { a.Amount, a.ForeignAmount }).ToListAsync();
        var openAmount = target.Amount - allocs.Sum(a => a.Amount);
        if (openAmount < amount) return null;
        if (foreignAmount is { } f)
        {
            var openForeign = (target.ForeignAmount ?? 0) - allocs.Sum(a => a.ForeignAmount ?? 0);
            if (openForeign < f || FxSettlementMath.BookValue(openForeign, openAmount, f, dp) != amount) return null;
        }
        return target.Id;
    }
}
