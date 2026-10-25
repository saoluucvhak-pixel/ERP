using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Fx;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Fx;

/// <summary>ApplyDiscount: hưởng/cấp chiết khấu thanh toán theo điều khoản của hóa đơn gốc (tính theo nguyên tệ của hóa đơn, bậc và hạn theo ngày thanh toán).</summary>
public sealed record FxSettleItem(Guid JournalLineId, decimal ForeignAmount, bool ApplyDiscount = false);

/// <summary>Receipt from a customer / payment to a supplier of foreign-currency invoices through a cash or bank account.</summary>
public sealed record FxPaymentInput(DateOnly Date, Guid CashAccountId, decimal PaymentRate, string Description, IReadOnlyList<FxSettleItem> Items);

/// <summary>Difference: + realised FX gain, − realised FX loss.</summary>
public sealed record FxPaymentPreview(string Currency, decimal ForeignTotal, decimal CashAmount, decimal BookAmount, decimal Difference,
    IReadOnlyList<VoucherLineInput> Lines, decimal DiscountForeign = 0, decimal DiscountBook = 0);

/// <summary>
/// Thu/chi ngoại tệ theo hóa đơn. Builds the voucher: the cash/bank line at the payment rate, one settling line per invoice at
/// that invoice's carrying amount for the foreign part paid, and the realised exchange difference on the accounts mapped to
/// roles FX_REALIZED_GAIN / FX_REALIZED_LOSS. The voucher is created as a Draft and goes through the normal approval flow;
/// posting re-checks the carrying amounts (FX_BOOK_VALUE), so a payment recorded in between cannot be double-counted.
/// A receipt books the cash line at the payment rate entered; a payment books it at the cash account's moving weighted-average
/// carrying rate (company decision 16/10/2026), so the realised difference is invoice carrying amount vs cash carrying amount.
/// </summary>
public sealed class FxPaymentService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string GainRole = "FX_REALIZED_GAIN";
    public const string LossRole = "FX_REALIZED_LOSS";

    public async Task<FxPaymentPreview> PreviewAsync(SessionContext ctx, FxPaymentInput input)
    {
        ctx.Require(Permissions.VoucherCreate);
        return await BuildAsync(ctx, input);
    }

    public async Task<Voucher> CreateDraftAsync(SessionContext ctx, FxPaymentInput input)
    {
        var preview = await BuildAsync(ctx, input);
        // Có chiết khấu: nguồn riêng để ghi sổ kiểm tra lại điều khoản / hạn / số tiền (PaymentDiscountService.CheckVoucherAsync) và khóa sửa tay.
        return await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, input.Date, input.Date, input.Description.Trim(), preview.Lines,
            SourceSystem: preview.DiscountForeign > 0 ? Hak.Application.Trade.PaymentDiscountService.Source : null, SourceId: preview.DiscountForeign > 0 ? "FX:" + Ids.New().ToString("N") : null));
    }

    private async Task<FxPaymentPreview> BuildAsync(SessionContext ctx, FxPaymentInput input)
    {
        ctx.Require(Permissions.VoucherCreate);
        if (string.IsNullOrWhiteSpace(input.Description)) throw new DomainException(ErrorCodes.ValidationFailed, "Diễn giải bắt buộc.");
        if (input.Items.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Chọn ít nhất một hóa đơn ngoại tệ cần thanh toán.");
        if (input.Items.Select(i => i.JournalLineId).Distinct().Count() != input.Items.Count)
            throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi hóa đơn chỉ chọn một lần.");
        if (input.Items.Any(i => i.ApplyDiscount) && !input.Items.All(i => i.ApplyDiscount))
            throw new DomainException(ErrorCodes.ValidationFailed, "Chiết khấu thanh toán: hoặc áp cho mọi hóa đơn trong chứng từ, hoặc không hóa đơn nào (tách chứng từ).");
        if (input.PaymentRate <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Tỷ giá thanh toán phải lớn hơn 0.");

        var settlement = new SettlementService(db, audit, clock);
        var dp = await settlement.FunctionalDecimalsAsync(ctx.CompanyId);
        var ids = input.Items.Select(i => i.JournalLineId).ToList();
        var targets = await db.JournalLines.AsNoTracking().Where(l => ids.Contains(l.Id) && l.CompanyId == ctx.CompanyId).ToListAsync();
        if (targets.Count != ids.Count) throw new DomainException(ErrorCodes.NotFound, "Có hóa đơn không tồn tại hoặc chưa ghi sổ.");
        var first = targets[0];
        if (first.ForeignCurrency is null || first.ForeignAmount is null)
            throw new DomainException(ErrorCodes.SettlementMismatch, "Chức năng này dùng cho công nợ gốc ngoại tệ.");
        if (targets.Any(t => t.AccountId != first.AccountId || t.PartnerId != first.PartnerId || t.ForeignCurrency != first.ForeignCurrency || t.Side != first.Side))
            throw new DomainException(ErrorCodes.SettlementMismatch, "Các hóa đơn phải cùng tài khoản công nợ, cùng đối tượng, cùng loại tiền và cùng chiều.");
        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == first.AccountId);
        if (account.OpenItemNature == OpenItemNature.None) throw new DomainException(ErrorCodes.SettlementMismatch, $"TK {account.Code} không theo dõi công nợ.");
        var cash = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.CashAccountId && a.CompanyId == ctx.CompanyId)
                   ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
        if (cash.OpenItemNature != OpenItemNature.None || cash.RequiresPartner)
            throw new DomainException(ErrorCodes.ValidationFailed, $"TK {cash.Code} không phải tài khoản tiền.");

        var currency = first.ForeignCurrency;
        var lines = new List<VoucherLineInput>();
        decimal bookTotal = 0, discountForeign = 0, discountBook = 0;
        var settleSide = first.Side == Side.Debit ? Side.Credit : Side.Debit;
        foreach (var item in input.Items)
        {
            var t = targets.Single(x => x.Id == item.JournalLineId);
            var (openForeign, open) = await settlement.OpenBothAsync(t);
            var book = FxSettlementMath.BookValue(openForeign, open, item.ForeignAmount, dp);
            var rate = FxSettlementMath.ExactRate(item.ForeignAmount, book, dp)
                       ?? throw new DomainException(ErrorCodes.FxBookValue, $"Không biểu diễn được giá trị ghi sổ {book} theo tỷ giá 6 chữ số; tách thành nhiều lần thanh toán.");
            lines.Add(new VoucherLineInput(t.AccountId, settleSide, book, t.PartnerId, currency, item.ForeignAmount, rate,
                $"Thanh toán {item.ForeignAmount} {currency} – {t.Description}", SettleJournalLineId: t.Id));
            bookTotal += book;
            if (item.ApplyDiscount)
            {
                var d = await Hak.Application.Trade.PaymentDiscountService.ForeignDiscountAsync(db, ctx.CompanyId, t, item.ForeignAmount, input.Date);
                discountForeign += d;
                discountBook += MoneyMath.Convert(d, rate, dp);              // ghi theo giá trị ghi sổ của hóa đơn: chênh lệch tỷ giá chỉ phát sinh ở phần tiền thực trả/thu
            }
        }
        var foreignTotal = input.Items.Sum(i => i.ForeignAmount) - discountForeign;           // tiền thực chuyển bằng nguyên tệ
        decimal cashAmount, cashRate;
        if (first.Side == Side.Credit)
        {
            // Paying out of a foreign-currency cash account: book value at the account's moving weighted-average rate
            // (company decision 16/10/2026); paying out the whole foreign balance takes its whole carrying amount.
            var (foreignBalance, bookBalance) = await CashBalanceAsync(cash.Id, currency, input.Date);
            if (foreignBalance < foreignTotal)
                throw new DomainException(ErrorCodes.ValidationFailed, $"Số dư {currency} của TK {cash.Code} ngày {input.Date:dd/MM/yyyy} là {foreignBalance}, không đủ chi {foreignTotal}.");
            cashAmount = FxSettlementMath.BookValue(foreignBalance, bookBalance, foreignTotal, dp);
            cashRate = FxSettlementMath.ExactRate(foreignTotal, cashAmount, dp)
                       ?? throw new DomainException(ErrorCodes.FxBookValue, $"Không biểu diễn được giá trị ghi sổ {cashAmount} theo tỷ giá 6 chữ số; tách thành nhiều lần chi.");
        }
        else
        {
            cashAmount = MoneyMath.Convert(foreignTotal, input.PaymentRate, dp);
            cashRate = input.PaymentRate;
        }
        lines.Insert(0, new VoucherLineInput(cash.Id, first.Side, cashAmount, null, currency, foreignTotal, cashRate, input.Description.Trim()));
        if (discountForeign > 0)
        {
            var dRole = first.Side == Side.Credit ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
            var dAcc = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == dRole).Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync()
                       ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {dRole}.");
            lines.Add(new VoucherLineInput(dAcc, first.Side, discountBook, null, Description: $"Chiết khấu thanh toán {discountForeign} {currency}"));
        }

        var signed = lines.Sum(l => (int)l.Side * l.Amount);
        if (signed != 0)
        {
            var role = signed > 0 ? GainRole : LossRole;
            var roleAccount = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role)
                                  .Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync()
                              ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role} (lãi/lỗ chênh lệch tỷ giá đã thực hiện).");
            lines.Add(new VoucherLineInput(roleAccount, signed > 0 ? Side.Credit : Side.Debit, Math.Abs(signed), null, Description:
                signed > 0 ? "Lãi chênh lệch tỷ giá đã thực hiện" : "Lỗ chênh lệch tỷ giá đã thực hiện"));
        }
        // Difference: + realised gain, − realised loss (receipt above book or payment below book is a gain).
        return new FxPaymentPreview(currency, foreignTotal, cashAmount, bookTotal, signed, lines, discountForeign, discountBook);
    }

    /// <summary>Foreign and functional-currency balance of a cash account in one currency up to and including <paramref name="date"/>.</summary>
    public async Task<(decimal Foreign, decimal Book)> CashBalanceAsync(Guid cashAccountId, string currency, DateOnly date)
    {
        var rows = await db.JournalLines.AsNoTracking().Where(l => l.AccountId == cashAccountId && l.ForeignCurrency == currency && l.PostingDate <= date)
            .GroupBy(l => l.Side).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount), Foreign = g.Sum(x => x.ForeignAmount ?? 0) }).ToListAsync();
        return (rows.Sum(r => (int)r.Key * r.Foreign), rows.Sum(r => (int)r.Key * r.Amount));
    }
}
