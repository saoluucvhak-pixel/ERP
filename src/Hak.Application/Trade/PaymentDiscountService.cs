using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Ledger;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record DiscountCandidate(Guid InvoiceId, string InvoiceNumber, string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate, Guid PartnerId, string PartnerCode, string PartnerName,
    decimal Percent, int Days, DateOnly Deadline, int DaysLeft, bool Expired, decimal Open, decimal Discount, decimal CashToPay, Guid JournalLineId, string? VoucherNumber, bool VoucherPending, string? TermsText = null);

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
    /// Chiết khấu bằng NGUYÊN TỆ của hóa đơn ngoại tệ: P% × tiền hàng (nguyên tệ, chưa thuế) × (phần thanh toán ÷ phải thu/phải trả nguyên tệ ban đầu), làm tròn theo số lẻ của đồng tiền;
    /// bậc và hạn theo ngày thanh toán. Hóa đơn ngoại tệ chỉ hỗ trợ cơ sở chưa thuế.
    /// </summary>
    internal static async Task<decimal> ForeignDiscountAsync(HakDbContext db, Guid companyId, JournalLine target, decimal foreignPaid, DateOnly payDate)
    {
        var voucherId = await db.JournalEntries.AsNoTracking().Where(e => e.Id == target.JournalEntryId).Select(e => (Guid?)e.VoucherId).FirstOrDefaultAsync();
        var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.VoucherId == voucherId && i.CompanyId == companyId);
        if (inv is null || inv.Status != InvoiceStatus.Posted || inv.Purpose != InvoicePurpose.Normal || inv.Currency is null || target.ForeignAmount is null || target.ForeignAmount == 0)
            throw new DomainException(ErrorCodes.ValidationFailed, "Chiết khấu thanh toán ngoại tệ chỉ áp cho khoản công nợ của hóa đơn ngoại tệ thường đã ghi sổ.");
        var itemForeign = target.ForeignAmount.Value;
        var terms = PaymentTerms.From(inv.PaymentDiscountPercent, inv.PaymentDiscountDays, inv.PaymentDiscountOptions);
        if (!terms.HasTerms) throw new DomainException(ErrorCodes.ValidationFailed, $"Hóa đơn {inv.Number} không có điều khoản chiết khấu thanh toán.");
        if (terms.IncludesVat) throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn ngoại tệ chỉ hỗ trợ chiết khấu thanh toán tính trên tiền hàng chưa thuế.");
        var ap = terms.Applicable(inv.InvoiceDate, payDate) ?? throw new DomainException(ErrorCodes.ValidationFailed, $"Ngày {payDate.Vn()} quá hạn hưởng chiết khấu thanh toán của hóa đơn {inv.Number} ({terms.Last(inv.InvoiceDate).Deadline.Vn()}).");
        var dp = await db.Currencies.Where(c => c.Code == inv.Currency).Select(c => c.DecimalPlaces).FirstAsync();
        var goods = inv.Lines.Sum(l => l.Amount);
        return Math.Min(MoneyMath.Round(goods * ap.Tier.Percent / 100m * (foreignPaid / itemForeign), dp), foreignPaid);
    }

    /// <summary>
    /// Chiết khấu thanh toán ĐỘC LẬP (chị chốt 25/10): không xuất hóa đơn nên không ghép với hóa đơn — chỉ là một dòng trong bút toán thanh toán riêng của đối tượng.
    /// Nguồn = "PARTNER:{đối tượng}#manual:{số chiết khấu}".
    /// </summary>
    internal static bool TryParsePartnerSource(string? sourceId, out Guid partnerId, out decimal discount)
    {
        partnerId = default; discount = 0;
        if (sourceId is null || !sourceId.StartsWith("PARTNER:", StringComparison.Ordinal)) return false;
        var cut = sourceId.IndexOf("#manual:", StringComparison.Ordinal);
        var tail = sourceId[(cut + 8)..]; var hash = tail.IndexOf('#');        // hậu tố "#<mã duy nhất>" để hai chứng từ cùng số tiền không trùng nguồn
        if (hash >= 0) tail = tail[..hash];
        return cut > 8 && Guid.TryParse(sourceId[8..cut], out partnerId)
            && decimal.TryParse(tail, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out discount);
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
    public async Task<Voucher> CreateStandaloneVoucherAsync(SessionContext ctx, InvoiceKind kind, Guid partnerId, Guid settleJournalLineId, Guid cashAccountId, DateOnly date, decimal discount, string evidence)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        // Không có hóa đơn / điều khoản làm căn cứ nên bắt buộc ghi chứng cứ tiền về (mã giao dịch ngân hàng hoặc số chứng từ) để người duyệt đối chiếu; chứng từ vẫn phải do người khác duyệt khi công ty bật "lập khác duyệt".
        evidence = (evidence ?? "").Trim();
        if (evidence.Length < 3) throw new DomainException(ErrorCodes.ValidationFailed, "Chiết khấu thanh toán độc lập phải ghi chứng cứ tiền về (mã giao dịch ngân hàng / số chứng từ).");
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
            var desc = $"{(purchase ? "Trả" : "Thu")} tiền {item.VoucherNumber} có chiết khấu thanh toán (chứng cứ: {evidence})";
            var lines = purchase
                ? new List<VoucherLineInput> { new(item.AccountId, Side.Debit, item.Open, partnerId, Description: desc, SettleJournalLineId: item.JournalLineId), new(cash.Id, Side.Credit, item.Open - discount, Description: desc) }
                : new List<VoucherLineInput> { new(cash.Id, Side.Debit, item.Open - discount, Description: desc), new(item.AccountId, Side.Credit, item.Open, partnerId, Description: desc, SettleJournalLineId: item.JournalLineId) };
            lines.Add(new VoucherLineInput(map.AccountId, purchase ? Side.Credit : Side.Debit, discount, Description: "Chiết khấu thanh toán"));
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, desc, lines, SourceSystem: Source,
                SourceId: $"PARTNER:{partnerId}#manual:{discount.ToString(System.Globalization.CultureInfo.InvariantCulture)}#{Ids.New():N}"));
            await audit.AppendAsync(ctx.CompanyId, ctx, "payment_discount.standalone", "partner", partnerId.ToString(), new { item.VoucherNumber, item.Open, discount, evidence, voucher = v.Number });
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
            var terms = PaymentTerms.From(i.PaymentDiscountPercent, i.PaymentDiscountDays, i.PaymentDiscountOptions);
            var applicable = terms.Applicable(i.InvoiceDate, asOf);
            var (tier, deadline) = applicable ?? terms.Last(i.InvoiceDate);          // quá mọi hạn: hiển thị bậc cuối, đánh dấu quá hạn
            var disc = ComputeDiscount(i, o.Amount, o.Open, tier.Percent);
            var p = partners[i.PartnerId];
            var dr = drafts.GetValueOrDefault(i.Id.ToString());
            rows.Add(new DiscountCandidate(i.Id, i.Number, i.InvoiceSeries, i.InvoiceNo, i.InvoiceDate, p.Id, p.Code, p.Name, tier.Percent, tier.Days, deadline,
                deadline.DayNumber - asOf.DayNumber, applicable is null, o.Open, disc, o.Open - disc, o.JournalLineId, dr?.Number, dr is { Status: not VoucherStatus.Posted }, terms.Describe()));
        }
        return rows.OrderBy(r => r.Expired).ThenBy(r => r.Deadline).ThenBy(r => r.InvoiceNumber, StringComparer.Ordinal).ToList();
    }

    /// <summary>Công thức chiết khấu dùng chung cho lúc lập chứng từ và lúc kiểm tra lại: <paramref name="itemAmount"/> = số phải thu / phải trả ban đầu, <paramref name="open"/> = số thanh toán.</summary>
    /// <summary>P% × cơ sở × (số còn mở ÷ số phải thu / phải trả ban đầu), cơ sở = tiền hàng chưa thuế, hoặc gồm thuế nếu điều khoản chọn; làm tròn theo tiền hạch toán. Thuế GTGT trên hóa đơn không đổi.</summary>
    internal static decimal ComputeDiscount(CommercialInvoice i, decimal itemAmount, decimal open, decimal percent)
    {
        var includesVat = PaymentTerms.From(i.PaymentDiscountPercent, i.PaymentDiscountDays, i.PaymentDiscountOptions).IncludesVat;
        var goods = i.Lines.Sum(l => l.FunctionalAmount + (includesVat ? l.VatAmount : 0));
        var raw = goods * percent / 100m * (itemAmount == 0 ? 0 : open / itemAmount);
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
        if (v.SourceId is not null && v.SourceId.StartsWith("FX:", StringComparison.Ordinal))
        {
            // Thu/chi ngoại tệ có chiết khấu: mọi dòng bù trừ đều hưởng chiết khấu; chiết khấu tính lại theo nguyên tệ, ngày ghi sổ và điều khoản của từng hóa đơn.
            var fxSettle = v.Lines.Where(l => l.SettleJournalLineId is not null).ToList();
            if (fxSettle.Count == 0) { Fail("Chứng từ chiết khấu ngoại tệ thiếu dòng bù trừ công nợ."); return; }
            var fdp = await (from c in db.Companies join cur in db.Currencies on c.FunctionalCurrency equals cur.Code where c.Id == v.CompanyId select cur.DecimalPlaces).FirstAsync();
            decimal expForeign = 0, expBook = 0, paidForeign = 0;
            foreach (var fs in fxSettle)
            {
                var target = await db.JournalLines.AsNoTracking().FirstOrDefaultAsync(j => j.Id == fs.SettleJournalLineId && j.CompanyId == v.CompanyId);
                if (target is null || fs.ForeignAmount is not { } f || fs.ExchangeRate is not { } r) { Fail("Dòng bù trừ ngoại tệ không hợp lệ."); return; }
                try { var d = await ForeignDiscountAsync(db, v.CompanyId, target, f, v.PostingDate); expForeign += d; expBook += MoneyMath.Convert(d, r, fdp); paidForeign += f; }
                catch (DomainException ex) { Fail(ex.Message); return; }
            }
            var dRoles = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == v.CompanyId && (m.RoleCode == "PAYMENT_DISCOUNT_INCOME" || m.RoleCode == "PAYMENT_DISCOUNT_EXPENSE")).Select(m => m.AccountId).ToListAsync();
            var bookDiscount = v.Lines.Where(l => l.SettleJournalLineId is null && l.ForeignAmount is null && dRoles.Contains(l.AccountId)).Sum(l => l.Amount);
            if (bookDiscount != expBook) Fail($"Số chiết khấu {bookDiscount} không khớp công thức ({expBook}) theo điều khoản của hóa đơn ngoại tệ.");
            var cashForeign = v.Lines.Where(l => l.SettleJournalLineId is null && l.ForeignAmount is not null).Sum(l => l.ForeignAmount!.Value);
            if (cashForeign != paidForeign - expForeign) Fail($"Số tiền thực trả/thu {cashForeign} (nguyên tệ) không khớp (phải bằng {paidForeign} trừ chiết khấu {expForeign}).");
            return;
        }
        var isMulti = v.SourceId is not null && v.SourceId.StartsWith("MULTI:", StringComparison.Ordinal);
        Guid sourceInvoice = default, sourcePartner = default;
        if (isMulti ? !(v.SourceId!.Length >= 42 && Guid.TryParse(v.SourceId[6..42], out sourcePartner)) : !Guid.TryParse(v.SourceId, out sourceInvoice)) { Fail("Chứng từ chiết khấu thanh toán thiếu hóa đơn nguồn."); return; }
        var settle = v.Lines.Where(l => l.SettleJournalLineId is not null).ToList();
        if (settle.Count == 0 || (!isMulti && settle.Count != 1)) { Fail(isMulti ? "Chứng từ chiết khấu thanh toán nhiều hóa đơn phải có dòng bù trừ công nợ." : "Chứng từ chiết khấu thanh toán phải có đúng một dòng bù trừ công nợ của hóa đơn."); return; }
        decimal expectedTotal = 0; InvoiceKind? kind = null; var seen = new HashSet<Guid>();
        foreach (var s in settle)
        {
            var target = await db.JournalLines.AsNoTracking().FirstOrDefaultAsync(j => j.Id == s.SettleJournalLineId && j.CompanyId == v.CompanyId);
            var targetVoucher = target is null ? (Guid?)null : await db.JournalEntries.AsNoTracking().Where(e => e.Id == target.JournalEntryId).Select(e => (Guid?)e.VoucherId).FirstOrDefaultAsync();
            var inv = target is null ? null : await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.VoucherId == targetVoucher && i.CompanyId == v.CompanyId);
            if (inv is null || inv.Status != InvoiceStatus.Posted || inv.Purpose != InvoicePurpose.Normal || (!isMulti && inv.Id != sourceInvoice) || (isMulti && inv.PartnerId != sourcePartner)
                || target!.PartnerId != inv.PartnerId || s.PartnerId != inv.PartnerId || !seen.Add(inv.Id))
            { Fail("Dòng bù trừ công nợ không thuộc hóa đơn nguồn còn hiệu lực (đã hủy/đảo, chưa ghi sổ, khác đối tượng hoặc trùng); hủy chứng từ này."); return; }
            if (kind is not null && kind != inv.Kind) { Fail("Không gộp hóa đơn mua và bán trong một chứng từ chiết khấu."); return; }
            kind = inv.Kind;
            if (v.PostingDate < inv.PostingDate) Fail("Ngày thanh toán không được trước ngày ghi sổ hóa đơn.");
            var terms = PaymentTerms.From(inv.PaymentDiscountPercent, inv.PaymentDiscountDays, inv.PaymentDiscountOptions);
            if (!terms.HasTerms)
            {
                if (!isMulti) { Fail("Hóa đơn nguồn không có điều khoản chiết khấu; hủy chứng từ này."); return; }
                continue;                                                    // gộp: hóa đơn không điều khoản chỉ được bù trừ, không có chiết khấu
            }
            var applicable = terms.Applicable(inv.InvoiceDate, v.PostingDate);
            if (applicable is null)
            {
                var last = terms.Last(inv.InvoiceDate).Deadline;
                if (!isMulti) Fail($"Ngày hạch toán {v.PostingDate.Vn()} quá hạn hưởng chiết khấu thanh toán ({last.Vn()}, tính từ ngày hóa đơn {inv.Number}); hủy chứng từ này và thanh toán như bình thường.");
                continue;                                                    // gộp: hóa đơn quá hạn không có chiết khấu
            }
            expectedTotal += ComputeDiscount(inv, target.Amount, s.Amount, applicable.Value.Tier.Percent);
        }
        var role = kind == InvoiceKind.Purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
        var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == v.CompanyId && m.RoleCode == role);
        var discountLines = v.Lines.Where(l => map is not null && l.AccountId == map.AccountId && l.SettleJournalLineId is null && l.PartnerId is null).ToList();
        var discount = discountLines.Sum(l => l.Amount);
        if (discount != expectedTotal) Fail($"Số chiết khấu {discount} không khớp công thức ({expectedTotal}) theo điều khoản của các hóa đơn nguồn.");
        var settleTotal = settle.Sum(l => l.Amount);
        var cash = v.Lines.Where(l => !settle.Contains(l) && !discountLines.Contains(l)).Sum(l => l.Amount);
        if (cash != settleTotal - expectedTotal) Fail($"Số tiền thực trả/thu {cash} không khớp (phải bằng số công nợ {settleTotal} trừ chiết khấu {expectedTotal}).");
    }

    /// <summary>
    /// Một chứng từ thanh toán cho NHIỀU hóa đơn của cùng đối tượng (một giấy báo Có / một lệnh chi): mỗi hóa đơn bù trừ trọn khoản còn mở, chiết khấu tính RIÊNG từng hóa đơn theo điều khoản của nó
    /// (hóa đơn không điều khoản hoặc quá hạn chỉ bù trừ, không có chiết khấu), tiền thực trả/thu = tổng công nợ − tổng chiết khấu. Nguồn "MULTI:{đối tượng}", kiểm tra lại khi ghi sổ.
    /// </summary>
    public async Task<Voucher> CreateMultiVoucherAsync(SessionContext ctx, InvoiceKind kind, Guid partnerId, IReadOnlyList<Guid> invoiceIds, Guid cashAccountId, DateOnly date)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        if (invoiceIds.Count < 2 || invoiceIds.Distinct().Count() != invoiceIds.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Chọn ít nhất hai hóa đơn khác nhau của cùng đối tượng.");
        return await db.InTransactionAsync(async () =>
        {
            var role = kind == InvoiceKind.Purchase ? "AP_TRADE" : "AR_TRADE";
            var arap = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role)
                       ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role}.");
            var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
                .Where(i => i.CompanyId == ctx.CompanyId && invoiceIds.Contains(i.Id) && i.Kind == kind && i.PartnerId == partnerId && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted && i.Currency == null && i.VoucherId != null).ToListAsync();
            if (invs.Count != invoiceIds.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Có hóa đơn không thuộc đối tượng / chiều đã chọn, chưa ghi sổ hoặc là hóa đơn ngoại tệ.");
            var open = (await new SettlementService(db, audit, clock).OpenItemsAsync(ctx, arap.AccountId, partnerId, date)).Where(o => o.Open > 0).GroupBy(o => o.VoucherId).ToDictionary(g => g.Key, g => g.First());
            var cash = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cashAccountId && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
            if (cash.CashKind is not (CashKind.Cash or CashKind.Bank)) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {cash.Code} không phải tài khoản tiền.");
            var purchase = kind == InvoiceKind.Purchase;
            var discRole = purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
            var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == discRole)
                      ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {discRole}.");
            var desc = $"{(purchase ? "Trả" : "Thu")} tiền {invs.Count} hóa đơn, có chiết khấu thanh toán theo từng hóa đơn";
            var settleLines = new List<VoucherLineInput>(); decimal totalOpen = 0, totalDisc = 0;
            foreach (var inv in invs.OrderBy(i => i.PostingDate).ThenBy(i => i.Number, StringComparer.Ordinal))
            {
                if (!open.TryGetValue(inv.VoucherId!.Value, out var o)) throw new DomainException(ErrorCodes.ValidationFailed, $"Hóa đơn {inv.Number} không còn công nợ mở.");
                if (date < inv.PostingDate) throw new DomainException(ErrorCodes.ValidationFailed, $"Ngày thanh toán không được trước ngày ghi sổ hóa đơn {inv.Number}.");
                var terms = PaymentTerms.From(inv.PaymentDiscountPercent, inv.PaymentDiscountDays, inv.PaymentDiscountOptions);
                var applicable = terms.HasTerms ? terms.Applicable(inv.InvoiceDate, date) : null;
                if (await db.Vouchers.AsNoTracking().AnyAsync(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == Source && v.SourceId == inv.Id.ToString() && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed))
                    throw new DomainException(ErrorCodes.DuplicateSource, $"Đã có chứng từ chiết khấu riêng cho hóa đơn {inv.Number}.");
                totalDisc += applicable is { } ap ? ComputeDiscount(inv, o.Amount, o.Open, ap.Tier.Percent) : 0;
                totalOpen += o.Open;
                settleLines.Add(purchase ? new(arap.AccountId, Side.Debit, o.Open, partnerId, Description: desc, SettleJournalLineId: o.JournalLineId)
                                         : new(arap.AccountId, Side.Credit, o.Open, partnerId, Description: desc, SettleJournalLineId: o.JournalLineId));
            }
            if (totalDisc <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không hóa đơn nào còn trong hạn hưởng chiết khấu tại ngày này; thanh toán như bình thường.");
            var lines = new List<VoucherLineInput>(settleLines)
            {
                purchase ? new(cash.Id, Side.Credit, totalOpen - totalDisc, Description: desc) : new(cash.Id, Side.Debit, totalOpen - totalDisc, Description: desc),
                new(map.AccountId, purchase ? Side.Credit : Side.Debit, totalDisc, Description: "Chiết khấu thanh toán"),
            };
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, desc, lines, SourceSystem: Source, SourceId: $"MULTI:{partnerId}:{Ids.New():N}"));
            await audit.AppendAsync(ctx.CompanyId, ctx, "payment_discount.multi", "partner", partnerId.ToString(), new { invoices = invs.Select(i => i.Number), totalOpen, discount = totalDisc, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
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
