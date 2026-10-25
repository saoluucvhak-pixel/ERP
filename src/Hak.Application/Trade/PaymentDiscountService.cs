using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record DiscountCandidate(Guid InvoiceId, string InvoiceNumber, string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate, Guid PartnerId, string PartnerCode, string PartnerName,
    decimal Percent, int Days, DateOnly Deadline, int DaysLeft, bool Expired, decimal Open, decimal Discount, decimal CashToPay, Guid JournalLineId, string? VoucherNumber, bool VoucherPending);

/// <summary>
/// Chiết khấu thanh toán (chu trình mua và bán): hóa đơn ghi điều khoản "P% nếu trả đủ trong N ngày kể từ ngày hóa đơn". Khoản còn mở trong hạn được liệt kê kèm số chiết khấu
/// (P% × tiền hàng chưa thuế, theo tỷ lệ phần còn mở / phần phải thu hoặc phải trả của hóa đơn) và số tiền thực trả / thực thu; lập chứng từ nháp thanh toán có chiết khấu:
/// mua — Nợ phải trả (bù trừ khoản mở) / Có tiền (trừ chiết khấu) / Có PAYMENT_DISCOUNT_INCOME; bán — Nợ tiền (trừ chiết khấu) / Nợ PAYMENT_DISCOUNT_EXPENSE / Có phải thu (bù trừ).
/// Chỉ cho hóa đơn tiền hạch toán; thuế GTGT giữ nguyên số trên hóa đơn [CẦN XÁC MINH cách xử lý hóa đơn / thuế khi có chiết khấu thanh toán theo quy định thuế hiện hành].
/// </summary>
public sealed class PaymentDiscountService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string Source = "HAK.PAYDISC";

    /// <summary>
    /// Chiết khấu thanh toán ĐỘC LẬP (chị chốt 25/10): không xuất hóa đơn nên không ghép với hóa đơn — chỉ là một dòng trong bút toán thanh toán riêng của đối tượng.
    /// Nguồn = "PARTNER:{đối tượng}#manual:{số chiết khấu}".
    /// </summary>
    internal static bool TryParsePartnerSource(string? sourceId, out Guid partnerId, out decimal discount)
    {
        partnerId = default; discount = 0;
        if (sourceId is null || !sourceId.StartsWith("PARTNER:", StringComparison.Ordinal)) return false;
        var cut = sourceId.IndexOf("#manual:", StringComparison.Ordinal);
        return cut > 8 && Guid.TryParse(sourceId[8..cut], out partnerId)
            && decimal.TryParse(sourceId[(cut + 8)..], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out discount);
    }

    /// <summary>Khoản phải thu / phải trả còn mở (tiền hạch toán) của đối tượng — chọn khoản để thanh toán kèm chiết khấu.</summary>
    public async Task<IReadOnlyList<OpenItem>> OpenItemsAsync(SessionContext ctx, InvoiceKind kind, Guid partnerId, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var role = kind == InvoiceKind.Purchase ? "AP_TRADE" : "AR_TRADE";
        var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role);
        if (map is null) return [];
        return (await new SettlementService(db, audit, clock).OpenItemsAsync(ctx, map.AccountId, partnerId, asOf)).Where(o => o.Open > 0 && o.ForeignCurrency is null).ToList();
    }

    /// <summary>Lập chứng từ nháp: bù trừ khoản mở đã chọn, tiền thực trả/thu = khoản mở − chiết khấu, phần chiết khấu vào PAYMENT_DISCOUNT_*. Không cần hóa đơn, không cần điều khoản.</summary>
    public async Task<Voucher> CreateStandaloneVoucherAsync(SessionContext ctx, InvoiceKind kind, Guid partnerId, Guid settleJournalLineId, Guid cashAccountId, DateOnly date, decimal discount)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        return await db.InTransactionAsync(async () =>
        {
            var item = (await OpenItemsAsync(ctx, kind, partnerId, date)).FirstOrDefault(o => o.JournalLineId == settleJournalLineId)
                       ?? throw new DomainException(ErrorCodes.ValidationFailed, "Khoản công nợ không còn mở hoặc không thuộc đối tượng này.");
            if (discount <= 0 || discount >= item.Open || discount != MoneyMath.Round(discount, 0))
                throw new DomainException(ErrorCodes.ValidationFailed, "Chiết khấu phải là số nguyên đồng, lớn hơn 0 và nhỏ hơn số công nợ còn mở.");
            var cash = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cashAccountId && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
            if (cash.CashKind is not (CashKind.Cash or CashKind.Bank)) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {cash.Code} không phải tài khoản tiền.");
            var purchase = kind == InvoiceKind.Purchase;
            var role = purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
            var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role)
                      ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role}.");
            var desc = $"{(purchase ? "Trả" : "Thu")} tiền {item.VoucherNumber} có chiết khấu thanh toán";
            var lines = purchase
                ? new List<VoucherLineInput> { new(item.AccountId, Side.Debit, item.Open, partnerId, Description: desc, SettleJournalLineId: item.JournalLineId), new(cash.Id, Side.Credit, item.Open - discount, Description: desc) }
                : new List<VoucherLineInput> { new(cash.Id, Side.Debit, item.Open - discount, Description: desc), new(item.AccountId, Side.Credit, item.Open, partnerId, Description: desc, SettleJournalLineId: item.JournalLineId) };
            lines.Add(new VoucherLineInput(map.AccountId, purchase ? Side.Credit : Side.Debit, discount, Description: "Chiết khấu thanh toán"));
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, desc, lines, SourceSystem: Source,
                SourceId: $"PARTNER:{partnerId}#manual:{discount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
            await audit.AppendAsync(ctx.CompanyId, ctx, "payment_discount.standalone", "partner", partnerId.ToString(), new { item.VoucherNumber, item.Open, discount, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    public async Task<IReadOnlyList<DiscountCandidate>> CandidatesAsync(SessionContext ctx, InvoiceKind kind, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var role = kind == InvoiceKind.Purchase ? "AP_TRADE" : "AR_TRADE";
        var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role);
        if (map is null) return [];
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == kind && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted && i.PaymentDiscountPercent != null && i.Currency == null && i.VoucherId != null).ToListAsync();
        if (invs.Count == 0) return [];
        var open = (await new SettlementService(db, audit, clock).OpenItemsAsync(ctx, map.AccountId, null, asOf)).GroupBy(o => o.VoucherId).ToDictionary(g => g.Key, g => g.First());
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var vids = invs.Select(i => i.Id.ToString()).ToList();
        var drafts = (await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == Source && v.SourceId != null && vids.Contains(v.SourceId) && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed)
            .Select(v => new { v.SourceId, v.Number, v.Status }).ToListAsync()).ToDictionary(v => v.SourceId!);
        var rows = new List<DiscountCandidate>();
        foreach (var i in invs)
        {
            if (!open.TryGetValue(i.VoucherId!.Value, out var o) || o.Open <= 0) continue;
            var deadline = i.InvoiceDate.AddDays(i.PaymentDiscountDays!.Value);
            var disc = Discount(i, o);
            var p = partners[i.PartnerId];
            var dr = drafts.GetValueOrDefault(i.Id.ToString());
            rows.Add(new DiscountCandidate(i.Id, i.Number, i.InvoiceSeries, i.InvoiceNo, i.InvoiceDate, p.Id, p.Code, p.Name, i.PaymentDiscountPercent!.Value, i.PaymentDiscountDays.Value, deadline,
                deadline.DayNumber - asOf.DayNumber, asOf > deadline, o.Open, disc, o.Open - disc, o.JournalLineId, dr?.Number, dr is { Status: not VoucherStatus.Posted }));
        }
        return rows.OrderBy(r => r.Expired).ThenBy(r => r.Deadline).ThenBy(r => r.InvoiceNumber, StringComparer.Ordinal).ToList();
    }

    /// <summary>P% × tiền hàng chưa thuế × (số còn mở ÷ số phải thu / phải trả ban đầu của hóa đơn), làm tròn theo tiền hạch toán.</summary>
    private static decimal Discount(CommercialInvoice i, OpenItem o) => ComputeDiscount(i, o.Amount, o.Open);

    /// <summary>Công thức chiết khấu dùng chung cho lúc lập chứng từ và lúc kiểm tra lại: <paramref name="itemAmount"/> = số phải thu / phải trả ban đầu, <paramref name="open"/> = số thanh toán.</summary>
    internal static decimal ComputeDiscount(CommercialInvoice i, decimal itemAmount, decimal open)
    {
        var goods = i.Lines.Sum(l => l.FunctionalAmount);
        var raw = goods * i.PaymentDiscountPercent!.Value / 100m * (itemAmount == 0 ? 0 : open / itemAmount);
        return Math.Min(MoneyMath.Round(raw, 0), open);
    }

    /// <summary>
    /// Kiểm tra lại điều kiện NGUỒN của chứng từ chiết khấu mỗi khi gửi duyệt / duyệt / ghi sổ (AccountingEngine gọi trong lúc kiểm tra chứng từ): hóa đơn còn hiệu lực,
    /// ngày hạch toán trong hạn hưởng chiết khấu (tính từ ngày hóa đơn), khoản công nợ đúng hóa đơn, số chiết khấu và số tiền thực trả/thu đúng công thức.
    /// Chứng từ nháp bị sửa số/ngày hoặc nguồn đổi sau khi lập thì không qua được.
    /// </summary>
    internal static async Task CheckVoucherAsync(HakDbContext db, Voucher v, List<ValidationError> errors)
    {
        void Fail(string message) => errors.Add(new(ErrorCodes.ValidationFailed, message));
        if (v.SourceId is not null && v.SourceId.StartsWith("PARTNER:", StringComparison.Ordinal))
        {
            if (!TryParsePartnerSource(v.SourceId, out var pid, out var d)) { Fail("Nguồn chứng từ chiết khấu thanh toán không hợp lệ."); return; }
            var settleLines = v.Lines.Where(l => l.SettleJournalLineId is not null).ToList();
            if (settleLines.Count != 1 || settleLines[0].PartnerId != pid) { Fail("Chứng từ chiết khấu thanh toán phải có đúng một dòng bù trừ công nợ của đối tượng."); return; }
            var sl = settleLines[0];
            if (d <= 0 || d >= sl.Amount) Fail("Chiết khấu phải lớn hơn 0 và nhỏ hơn số công nợ thanh toán.");
            var discountRoles = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == v.CompanyId && (m.RoleCode == "PAYMENT_DISCOUNT_INCOME" || m.RoleCode == "PAYMENT_DISCOUNT_EXPENSE")).Select(m => m.AccountId).ToListAsync();
            var dl = v.Lines.Where(l => l != sl && l.SettleJournalLineId is null && l.PartnerId is null && discountRoles.Contains(l.AccountId)).ToList();
            if (dl.Count != 1 || dl[0].Amount != d) Fail($"Số chiết khấu không khớp số đã nhập ({d}).");
            var cashTotal = v.Lines.Where(l => l != sl && !dl.Contains(l)).Sum(l => l.Amount);
            if (cashTotal != sl.Amount - d) Fail($"Số tiền thực trả/thu {cashTotal} không khớp (phải bằng số công nợ {sl.Amount} trừ chiết khấu {d}).");
            return;
        }
        if (!Guid.TryParse(v.SourceId, out var invoiceId)) { Fail("Chứng từ chiết khấu thanh toán thiếu hóa đơn nguồn."); return; }
        var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == v.CompanyId);
        if (inv is null || inv.Status != InvoiceStatus.Posted || inv.Purpose != InvoicePurpose.Normal || inv.PaymentDiscountPercent is null || inv.PaymentDiscountDays is null)
        { Fail("Hóa đơn nguồn của chứng từ chiết khấu thanh toán không còn hiệu lực (đã hủy/đảo, chưa ghi sổ hoặc không có điều khoản chiết khấu); hủy chứng từ này."); return; }
        var deadline = inv.InvoiceDate.AddDays(inv.PaymentDiscountDays.Value);
        if (v.PostingDate > deadline)
            Fail($"Ngày hạch toán {v.PostingDate.Vn()} quá hạn hưởng chiết khấu thanh toán ({deadline.Vn()}, tính từ ngày hóa đơn {inv.Number}); hủy chứng từ này và thanh toán như bình thường.");
        if (v.PostingDate < inv.PostingDate) Fail("Ngày thanh toán không được trước ngày ghi sổ hóa đơn.");

        var settle = v.Lines.Where(l => l.SettleJournalLineId is not null).ToList();
        if (settle.Count != 1) { Fail("Chứng từ chiết khấu thanh toán phải có đúng một dòng bù trừ công nợ của hóa đơn."); return; }
        var s = settle[0];
        var target = await db.JournalLines.AsNoTracking().FirstOrDefaultAsync(j => j.Id == s.SettleJournalLineId && j.CompanyId == v.CompanyId);
        var targetVoucher = target is null ? (Guid?)null : await db.JournalEntries.AsNoTracking().Where(e => e.Id == target.JournalEntryId).Select(e => (Guid?)e.VoucherId).FirstOrDefaultAsync();
        if (target is null || targetVoucher != inv.VoucherId || target.PartnerId != inv.PartnerId || s.PartnerId != inv.PartnerId)
        { Fail("Dòng bù trừ công nợ không thuộc hóa đơn nguồn."); return; }

        var expected = ComputeDiscount(inv, target.Amount, s.Amount);
        var role = inv.Kind == InvoiceKind.Purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
        var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == v.CompanyId && m.RoleCode == role);
        var discountLines = v.Lines.Where(l => map is not null && l.AccountId == map.AccountId && l.SettleJournalLineId is null && l.PartnerId is null).ToList();
        var discount = discountLines.Sum(l => l.Amount);
        if (discount != expected) Fail($"Số chiết khấu {discount} không khớp công thức ({expected}) theo điều khoản {inv.PaymentDiscountPercent}% của hóa đơn {inv.Number}.");
        var cash = v.Lines.Where(l => l != s && !discountLines.Contains(l)).Sum(l => l.Amount);
        if (cash != s.Amount - expected) Fail($"Số tiền thực trả/thu {cash} không khớp (phải bằng số công nợ {s.Amount} trừ chiết khấu {expected}).");
    }

    public async Task<Voucher> CreateVoucherAsync(SessionContext ctx, Guid invoiceId, Guid cashAccountId, DateOnly date)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        return await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            var cand = (await CandidatesAsync(ctx, inv.Kind, date)).FirstOrDefault(c => c.InvoiceId == invoiceId)
                       ?? throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn không có chiết khấu thanh toán, đã thanh toán hết, hoặc không phải hóa đơn tiền hạch toán đã ghi sổ.");
            if (cand.Expired) throw new DomainException(ErrorCodes.ValidationFailed, $"Đã quá hạn hưởng chiết khấu ({cand.Deadline.Vn()}); thanh toán như bình thường.");
            if (date < inv.PostingDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày thanh toán không được trước ngày ghi sổ hóa đơn.");
            if (cand.VoucherNumber is not null) throw new DomainException(ErrorCodes.DuplicateSource, $"Đã có chứng từ {cand.VoucherNumber} cho hóa đơn này.");
            var cash = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cashAccountId && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
            if (cash.CashKind is not (CashKind.Cash or CashKind.Bank)) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {cash.Code} không phải tài khoản tiền.");
            var purchase = inv.Kind == InvoiceKind.Purchase;
            var role = purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
            var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role)
                      ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role} ({(purchase ? "chiết khấu thanh toán được hưởng" : "chiết khấu thanh toán cho khách")}).");
            var arap = await db.JournalLines.AsNoTracking().FirstAsync(l => l.Id == cand.JournalLineId);
            var d = cand.Discount;
            var desc = $"{(purchase ? "Trả" : "Thu")} tiền HĐ {inv.InvoiceSeries}/{inv.InvoiceNo} có chiết khấu thanh toán {cand.Percent}%";
            var lines = purchase
                ? new List<VoucherLineInput> { new(arap.AccountId, Side.Debit, cand.Open, inv.PartnerId, Description: desc, SettleJournalLineId: cand.JournalLineId), new(cash.Id, Side.Credit, cand.Open - d, Description: desc) }
                : new List<VoucherLineInput> { new(cash.Id, Side.Debit, cand.Open - d, Description: desc), new(arap.AccountId, Side.Credit, cand.Open, inv.PartnerId, Description: desc, SettleJournalLineId: cand.JournalLineId) };
            if (d > 0) lines.Add(new VoucherLineInput(map.AccountId, purchase ? Side.Credit : Side.Debit, d, Description: $"Chiết khấu thanh toán {inv.Number}"));
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, desc, lines, SourceSystem: Source, SourceId: inv.Id.ToString()));
            await audit.AppendAsync(ctx.CompanyId, ctx, "payment_discount.voucher", "invoice", inv.Id.ToString(), new { inv.Number, cand.Open, discount = d, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }
}
