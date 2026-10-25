using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record AgreementTierInput(decimal Threshold, decimal Percent);

public sealed record AgreementInput(InvoiceKind Kind, Guid PartnerId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AgreementTierInput> Tiers);

public sealed record AgreementView(Guid Id, InvoiceKind Kind, Guid PartnerId, string PartnerCode, string PartnerName, string Code, string Name, DateOnly FromDate, DateOnly ToDate,
    bool IsActive, long RowVersion, IReadOnlyList<AgreementTierInput> Tiers, Guid? RunId, decimal? RunAmount);

public sealed record AgreementInvoiceShare(Guid InvoiceId, string Number, string InvoiceSeries, string InvoiceNo, DateOnly PostingDate, decimal Sales, decimal Discount);

public sealed record AgreementPreview(Guid AgreementId, decimal EligibleSales, decimal Percent, decimal Amount, decimal? NextThreshold, decimal? NextPercent, bool Settled,
    IReadOnlyList<AgreementInvoiceShare> Invoices);

/// <summary>
/// Chiết khấu thương mại theo doanh số cuối kỳ (rebate): thỏa thuận có bậc, tính trên doanh số hàng chưa thuế (tiền hạch toán) của hóa đơn thường đã ghi sổ trong khoảng ngày, trừ hàng trả lại / giảm giá đã ghi sổ.
/// Hóa đơn ngoại tệ tính doanh số theo tiền hạch toán nhưng chiết khấu từng dòng theo tiền của hóa đơn (và tỷ giá của chính nó). Quyết toán sinh hóa đơn điều chỉnh giảm NHÁP (loại chiết khấu thương mại) cho từng hóa đơn gốc, chia theo tỷ lệ doanh số và theo từng dòng
/// để thuế GTGT giảm đúng thuế suất của dòng; ghi sổ hóa đơn do người có quyền ghi sổ làm như mọi điều chỉnh khác (bên bán: Nợ chiết khấu thương mại; bên mua: giảm hàng tồn kho / giá vốn).
/// Mỗi thỏa thuận chỉ có một lần quyết toán còn hiệu lực. Lập / sửa thỏa thuận là hành vi của người có thẩm quyền duyệt nên cần quyền duyệt chứng từ.
/// </summary>
public sealed class DiscountAgreementService(HakDbContext db, InvoiceService invoices, AuditService audit, IClock clock)
{
    private static string Norm(string s) => s.Trim();

    public async Task<IReadOnlyList<AgreementView>> ListAsync(SessionContext ctx, InvoiceKind kind)
    {
        ctx.Require(Permissions.ReportView);
        var list = await db.DiscountAgreements.AsNoTracking().Include(a => a.Tiers).Where(a => a.CompanyId == ctx.CompanyId && a.Kind == kind).OrderByDescending(a => a.FromDate).ThenBy(a => a.Code).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var runs = await db.DiscountAgreementRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId && r.VoidedAt == null).ToDictionaryAsync(r => r.AgreementId);
        return list.Select(a => new AgreementView(a.Id, a.Kind, a.PartnerId, partners[a.PartnerId].Code, partners[a.PartnerId].Name, a.Code, a.Name, a.FromDate, a.ToDate, a.IsActive, a.RowVersion,
            a.Tiers.OrderBy(t => t.Threshold).Select(t => new AgreementTierInput(t.Threshold, t.Percent)).ToList(), runs.GetValueOrDefault(a.Id)?.Id, runs.GetValueOrDefault(a.Id)?.Amount)).ToList();
    }

    private async Task<int> FunctionalDpAsync(Guid companyId)
        => await (from c in db.Companies join cur in db.Currencies on c.FunctionalCurrency equals cur.Code where c.Id == companyId select cur.DecimalPlaces).FirstAsync();

    private static void Validate(AgreementInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Code) || string.IsNullOrWhiteSpace(i.Name)) throw new DomainException(ErrorCodes.ValidationFailed, "Thỏa thuận phải có mã và tên.");
        if (i.ToDate < i.FromDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày kết thúc không được trước ngày bắt đầu.");
        if (i.Tiers.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Thỏa thuận phải có ít nhất một bậc chiết khấu.");
        foreach (var t in i.Tiers)
        {
            if (t.Threshold < 0 || t.Percent <= 0 || t.Percent > 100 || !MoneyMath.HasAtMostDecimals(t.Percent, 4) || !MoneyMath.HasAtMostDecimals(t.Threshold, 4))
                throw new DomainException(ErrorCodes.ValidationFailed, "Bậc chiết khấu: ngưỡng doanh số ≥ 0, tỷ lệ lớn hơn 0 đến 100% (tối đa 4 chữ số thập phân).");
        }
        if (i.Tiers.Select(t => t.Threshold).Distinct().Count() != i.Tiers.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Hai bậc không được cùng ngưỡng doanh số.");
        var ordered = i.Tiers.OrderBy(t => t.Threshold).ToList();
        for (var k = 1; k < ordered.Count; k++)
            if (ordered[k].Percent < ordered[k - 1].Percent) throw new DomainException(ErrorCodes.ValidationFailed, "Bậc doanh số cao hơn không được có tỷ lệ chiết khấu thấp hơn bậc dưới.");
    }

    public async Task<DiscountAgreement> CreateAsync(SessionContext ctx, AgreementInput input)
    {
        ctx.Require(Permissions.VoucherApprove);
        Validate(input);
        return await db.InTransactionAsync(async () =>
        {
            if (!await db.BusinessPartners.AnyAsync(p => p.Id == input.PartnerId && p.CompanyId == ctx.CompanyId)) throw new DomainException(ErrorCodes.PartnerWrongCompany, "Đối tượng không thuộc công ty.");
            var code = Norm(input.Code);
            if (await db.DiscountAgreements.AnyAsync(a => a.CompanyId == ctx.CompanyId && a.Code == code)) throw new DomainException(ErrorCodes.DuplicateCode, $"Mã thỏa thuận {code} đã tồn tại.");
            var a = new DiscountAgreement
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Kind = input.Kind, PartnerId = input.PartnerId, Code = code, Name = Norm(input.Name), FromDate = input.FromDate, ToDate = input.ToDate,
                CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
            };
            foreach (var t in input.Tiers) a.Tiers.Add(new DiscountAgreementTier { Id = Ids.New(), AgreementId = a.Id, CompanyId = ctx.CompanyId, Threshold = t.Threshold, Percent = t.Percent });
            db.DiscountAgreements.Add(a);
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.create", "discount_agreement", a.Id.ToString(), new { a.Code, a.Kind, input.PartnerId, a.FromDate, a.ToDate, tiers = input.Tiers });
            await db.SaveChangesAsync();
            return a;
        });
    }

    public async Task<DiscountAgreement> UpdateAsync(SessionContext ctx, Guid id, long expectedRowVersion, AgreementInput input)
    {
        ctx.Require(Permissions.VoucherApprove);
        Validate(input);
        return await db.InTransactionAsync(async () =>
        {
            var a = await db.DiscountAgreements.Include(x => x.Tiers).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thỏa thuận không tồn tại.");
            RowVersionCheck.Require(a.RowVersion, expectedRowVersion, $"Thỏa thuận {a.Code}");
            if (await db.DiscountAgreementRuns.AnyAsync(r => r.AgreementId == id && r.VoidedAt == null)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thỏa thuận {a.Code} đã quyết toán; hủy lần quyết toán trước khi sửa.");
            if (a.Kind != input.Kind || a.PartnerId != input.PartnerId) throw new DomainException(ErrorCodes.ValidationFailed, "Không đổi chiều mua/bán hoặc đối tượng của thỏa thuận; lập thỏa thuận mới.");
            var code = Norm(input.Code);
            if (code != a.Code && await db.DiscountAgreements.AnyAsync(x => x.CompanyId == ctx.CompanyId && x.Code == code && x.Id != id)) throw new DomainException(ErrorCodes.DuplicateCode, $"Mã thỏa thuận {code} đã tồn tại.");
            a.Code = code; a.Name = Norm(input.Name); a.FromDate = input.FromDate; a.ToDate = input.ToDate;
            db.DiscountAgreementTiers.RemoveRange(a.Tiers);
            var tiers = input.Tiers.Select(t => new DiscountAgreementTier { Id = Ids.New(), AgreementId = a.Id, CompanyId = ctx.CompanyId, Threshold = t.Threshold, Percent = t.Percent }).ToList();
            db.DiscountAgreementTiers.AddRange(tiers);
            a.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.update", "discount_agreement", a.Id.ToString(), new { a.Code, a.FromDate, a.ToDate, tiers = input.Tiers }, a.RowVersion);
            await db.SaveChangesAsync();
            return a;
        });
    }

    public async Task<DiscountAgreement> SetActiveAsync(SessionContext ctx, Guid id, long expectedRowVersion, bool active)
    {
        ctx.Require(Permissions.VoucherApprove);
        return await db.InTransactionAsync(async () =>
        {
            var a = await db.DiscountAgreements.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thỏa thuận không tồn tại.");
            RowVersionCheck.Require(a.RowVersion, expectedRowVersion, $"Thỏa thuận {a.Code}");
            a.IsActive = active; a.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, active ? "discount_agreement.activate" : "discount_agreement.deactivate", "discount_agreement", a.Id.ToString(), new { a.Code }, a.RowVersion);
            await db.SaveChangesAsync();
            return a;
        });
    }

    /// <summary>Lines: số chiết khấu từng dòng hóa đơn gốc theo TIỀN CỦA HÓA ĐƠN (bằng tiền hạch toán với hóa đơn trong nước); Discount: tổng quy ra tiền hạch toán.</summary>
    private sealed record Plan(DiscountAgreement Agreement, decimal Eligible, decimal Percent, decimal Amount, decimal? NextThreshold, decimal? NextPercent,
        List<(CommercialInvoice Invoice, decimal Sales, decimal Discount, List<(int LineNo, decimal Amount)> Lines)> Shares);

    /// <summary>Tính doanh số đủ điều kiện, bậc đạt, số chiết khấu và cách chia theo hóa đơn / dòng. Chỉ đọc.</summary>
    private async Task<Plan> PlanAsync(SessionContext ctx, DiscountAgreement a)
    {
        var dp = await FunctionalDpAsync(ctx.CompanyId);
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == a.Kind && i.PartnerId == a.PartnerId && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted
                        && i.PostingDate >= a.FromDate && i.PostingDate <= a.ToDate)
            .OrderBy(i => i.PostingDate).ThenBy(i => i.Number).ToListAsync();
        var ids = invs.Select(i => i.Id).ToList();
        // Điều chỉnh giảm / trả hàng đã có (bỏ các hóa đơn thuộc lần quyết toán còn hiệu lực của chính thỏa thuận này để xem lại số liệu sau khi quyết toán vẫn ổn định).
        var own = await db.DiscountAgreementRunInvoices.AsNoTracking().Where(r => db.DiscountAgreementRuns.Any(x => x.Id == r.RunId && x.AgreementId == a.Id && x.VoidedAt == null)).Select(r => r.AdjustmentInvoiceId).ToListAsync();
        var adj = await (from l in db.CommercialInvoiceLines.AsNoTracking()
                         join i in db.CommercialInvoices.AsNoTracking() on l.InvoiceId equals i.Id
                         where i.CompanyId == ctx.CompanyId && i.AdjustsInvoiceId != null && ids.Contains(i.AdjustsInvoiceId.Value) && !own.Contains(i.Id)
                               && (i.Purpose == InvoicePurpose.Return || i.Purpose == InvoicePurpose.Discount) && (i.Status == InvoiceStatus.Posted || i.Status == InvoiceStatus.Draft)
                         select new { Orig = i.AdjustsInvoiceId!.Value, l.AdjustsLineNo, i.Status, l.FunctionalAmount, l.Amount }).ToListAsync();

        // net = doanh số tính chiết khấu (chỉ điều chỉnh đã ghi sổ); room = chỗ còn giảm được trên dòng (tính cả điều chỉnh nháp). Hóa đơn ngoại tệ có thêm số theo tiền hóa đơn.
        var perInvoice = new List<(CommercialInvoice Inv, List<(int LineNo, decimal Net, decimal Room, decimal NetFx, decimal RoomFx)> Lines)>();
        foreach (var i in invs)
        {
            var lines = i.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var mine = adj.Where(x => x.Orig == i.Id && x.AdjustsLineNo == l.LineNo).ToList();
                var posted = mine.Where(x => x.Status == InvoiceStatus.Posted);
                return (l.LineNo, Net: Math.Max(0, l.FunctionalAmount - posted.Sum(x => x.FunctionalAmount)), Room: Math.Max(0, l.FunctionalAmount - mine.Sum(x => x.FunctionalAmount)),
                        NetFx: Math.Max(0, l.Amount - posted.Sum(x => x.Amount)), RoomFx: Math.Max(0, l.Amount - mine.Sum(x => x.Amount)));
            }).ToList();
            perInvoice.Add((i, lines));
        }
        var eligible = perInvoice.Sum(p => p.Lines.Sum(l => l.Net));
        var tiers = a.Tiers.OrderBy(t => t.Threshold).ToList();
        var reached = tiers.LastOrDefault(t => t.Threshold <= eligible);
        var next = tiers.FirstOrDefault(t => t.Threshold > eligible);
        var percent = reached?.Percent ?? 0;
        var shares = new List<(CommercialInvoice, decimal, decimal, List<(int, decimal)>)>();

        // Hóa đơn tiền hạch toán: chia theo tỷ lệ doanh số (hóa đơn cuối nhận phần dư) rồi theo dòng (dòng cuối nhận phần dư) để tổng đúng bằng Round(doanh số × %).
        var domestic = perInvoice.Where(p => p.Inv.Currency == null && p.Lines.Sum(l => l.Net) > 0).ToList();
        var domesticSales = domestic.Sum(p => p.Lines.Sum(l => l.Net));
        var amount = MoneyMath.Round(domesticSales * percent / 100m, dp);
        var remaining = amount;
        for (var k = 0; k < domestic.Count; k++)
        {
            var (inv, lines) = domestic[k];
            var sales = lines.Sum(l => l.Net);
            var invAmount = k == domestic.Count - 1 ? remaining : Math.Min(remaining, MoneyMath.Round(amount * sales / domesticSales, dp));
            remaining -= invAmount;
            var lineShares = new List<(int, decimal)>();
            var left = invAmount;
            var live = lines.Where(l => l.Net > 0).ToList();
            for (var j = 0; j < live.Count; j++)
            {
                var l = live[j];
                var share = j == live.Count - 1 ? left : Math.Min(left, MoneyMath.Round(invAmount * l.Net / sales, dp));
                share = Math.Min(share, l.Room);
                left -= share;
                if (share > 0) lineShares.Add((l.LineNo, share));
            }
            shares.Add((inv, sales, invAmount - left, lineShares));
        }
        // Hóa đơn ngoại tệ: chiết khấu từng dòng = % × tiền hàng dòng THEO TIỀN HÓA ĐƠN (tính tròn theo số lẻ của đồng tiền đó), quy ra tiền hạch toán theo tỷ giá của chính hóa đơn.
        foreach (var (inv, lines) in perInvoice.Where(p => p.Inv.Currency != null && p.Lines.Sum(l => l.Net) > 0))
        {
            var curDp = await db.Currencies.Where(c => c.Code == inv.Currency).Select(c => c.DecimalPlaces).FirstAsync();
            var lineShares = new List<(int, decimal)>(); decimal functional = 0;
            foreach (var l in lines.Where(l => l.NetFx > 0))
            {
                var share = Math.Min(MoneyMath.Round(l.NetFx * percent / 100m, curDp), l.RoomFx);
                if (share <= 0) continue;
                lineShares.Add((l.LineNo, share));
                functional += MoneyMath.Convert(share, inv.ExchangeRate!.Value, dp);
            }
            shares.Add((inv, lines.Sum(l => l.Net), functional, lineShares));
        }
        return new Plan(a, eligible, percent, shares.Sum(s => s.Item3), next?.Threshold, next?.Percent, shares.OrderBy(s => s.Item1.PostingDate).ThenBy(s => s.Item1.Number, StringComparer.Ordinal).ToList());
    }

    private async Task<DiscountAgreement> LoadAsync(SessionContext ctx, Guid id)
        => await db.DiscountAgreements.AsNoTracking().Include(x => x.Tiers).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thỏa thuận không tồn tại.");

    public async Task<AgreementPreview> PreviewAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var a = await LoadAsync(ctx, id);
        var plan = await PlanAsync(ctx, a);
        var settled = await db.DiscountAgreementRuns.AnyAsync(r => r.AgreementId == id && r.VoidedAt == null);
        return new AgreementPreview(id, plan.Eligible, plan.Percent, plan.Amount, plan.NextThreshold, plan.NextPercent, settled,
            plan.Shares.Select(s => new AgreementInvoiceShare(s.Invoice.Id, s.Invoice.Number, s.Invoice.InvoiceSeries, s.Invoice.InvoiceNo, s.Invoice.PostingDate, s.Sales, s.Discount)).ToList());
    }

    /// <summary>Số hóa đơn kế tiếp: số thuần thì tăng và giữ độ rộng (0001 → 0002); chuỗi khác thì thêm hậu tố thứ tự.</summary>
    internal static string InvoiceNoAt(string first, int index)
        => long.TryParse(first, out var n) && first.All(char.IsDigit) ? (n + index).ToString().PadLeft(first.Length, '0') : $"{first}-{index + 1}";

    /// <summary>
    /// Quyết toán: sinh hóa đơn điều chỉnh giảm NHÁP (chiết khấu thương mại) cho từng hóa đơn gốc. Số hóa đơn điều chỉnh đánh liên tiếp từ <paramref name="firstInvoiceNo"/>
    /// với ký hiệu <paramref name="invoiceSeries"/>; hóa đơn điều chỉnh của bên mua mang số chứng từ nhà cung cấp phát hành nên nhập số đầu tiên theo chứng từ nhận được.
    /// </summary>
    public async Task<DiscountAgreementRun> SettleAsync(SessionContext ctx, Guid id, long expectedRowVersion, string invoiceSeries, string firstInvoiceNo, DateOnly invoiceDate, DateOnly postingDate)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(invoiceSeries) || string.IsNullOrWhiteSpace(firstInvoiceNo)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập ký hiệu và số hóa đơn điều chỉnh đầu tiên.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            RowVersionCheck.Require(a.RowVersion, expectedRowVersion, $"Thỏa thuận {a.Code}");
            if (!a.IsActive) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thỏa thuận {a.Code} đã ngừng áp dụng.");
            if (invoiceDate < a.ToDate || postingDate < a.ToDate)
                throw new DomainException(ErrorCodes.ValidationFailed, $"Chỉ quyết toán sau khi hết kỳ thỏa thuận ({a.ToDate.Vn()}); ngày hóa đơn và ngày hạch toán không được trước ngày đó.");
            if (await db.DiscountAgreementRuns.AnyAsync(r => r.AgreementId == id && r.VoidedAt == null))
                throw new DomainException(ErrorCodes.DuplicateSource, $"Thỏa thuận {a.Code} đã quyết toán; hủy lần quyết toán cũ nếu cần làm lại.");
            var plan = await PlanAsync(ctx, a);
            if (plan.Amount <= 0) throw new DomainException(ErrorCodes.ValidationFailed, plan.Percent == 0
                ? $"Doanh số {plan.Eligible.Vn(0)} chưa đạt bậc chiết khấu nào của thỏa thuận {a.Code}."
                : "Không có số chiết khấu phát sinh.");
            var run = new DiscountAgreementRun { Id = Ids.New(), CompanyId = ctx.CompanyId, AgreementId = a.Id, EligibleSales = plan.Eligible, Percent = plan.Percent, Amount = plan.Amount, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.DiscountAgreementRuns.Add(run);
            var n = 0;
            foreach (var share in plan.Shares.Where(s => s.Lines.Count > 0))
            {
                var inv = await invoices.CreateAdjustmentAsync(ctx, new AdjustmentInput(share.Invoice.Id, InvoicePurpose.Discount, invoiceSeries.Trim(), InvoiceNoAt(firstInvoiceNo.Trim(), n++), invoiceDate, postingDate,
                    $"Chiết khấu thương mại theo thỏa thuận {a.Code} ({a.FromDate.Vn()} – {a.ToDate.Vn()}), đạt {plan.Percent}%",
                    share.Lines.Select(l => new AdjustmentLineInput(l.LineNo, Amount: l.Amount, Description: $"Chiết khấu thương mại {plan.Percent}% – {a.Name}")).ToList(),
                    DiscountKind.Trade, UnderAgreement: true));
                db.DiscountAgreementRunInvoices.Add(new DiscountAgreementRunInvoice { Id = Ids.New(), RunId = run.Id, CompanyId = ctx.CompanyId, OriginalInvoiceId = share.Invoice.Id, AdjustmentInvoiceId = inv.Id, Amount = share.Discount });
            }
            await db.SaveChangesAsync();
            // Số thật của các hóa đơn điều chỉnh vừa tạo (tiền hạch toán) phải đúng bằng số đã tính; lệch nghĩa là có dòng đã giảm gần hết.
            var createdIds = await db.DiscountAgreementRunInvoices.Where(r => r.RunId == run.Id).Select(r => r.AdjustmentInvoiceId).ToListAsync();
            var actual = (await db.CommercialInvoiceLines.AsNoTracking().Where(l => createdIds.Contains(l.InvoiceId)).Select(l => l.FunctionalAmount).ToListAsync()).Sum();
            var drift = plan.Amount - actual;
            if (drift != 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Không chia hết số chiết khấu {plan.Amount.Vn(0)} lên các dòng hóa đơn (lệch {drift.Vn(0)}: dòng đã giảm gần hết); kiểm tra các điều chỉnh nháp đang có.");
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.settle", "discount_agreement", a.Id.ToString(), new { a.Code, run.EligibleSales, run.Percent, run.Amount, invoices = n });
            await db.SaveChangesAsync();
            return run;
        });
    }

    /// <summary>Hủy lần quyết toán để làm lại: hủy các hóa đơn điều chỉnh nháp đã sinh; từ chối nếu có hóa đơn đã ghi sổ (phải đảo hóa đơn đó trước).</summary>
    public async Task VoidRunAsync(SessionContext ctx, Guid agreementId, string reason)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var run = await db.DiscountAgreementRuns.Include(r => r.Invoices).FirstOrDefaultAsync(r => r.AgreementId == agreementId && r.CompanyId == ctx.CompanyId && r.VoidedAt == null)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Thỏa thuận chưa quyết toán.");
            var ids = run.Invoices.Select(i => i.AdjustmentInvoiceId).ToList();
            var posted = await db.CommercialInvoices.AsNoTracking().Where(i => ids.Contains(i.Id) && i.Status == InvoiceStatus.Posted).Select(i => i.Number).ToListAsync();
            if (posted.Count > 0) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đã ghi sổ hóa đơn điều chỉnh {string.Join(", ", posted)}; đảo các hóa đơn này trước khi hủy quyết toán.");
            foreach (var id in ids)
            {
                var inv = await db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == id);
                if (inv.Status == InvoiceStatus.Draft) await invoices.CancelAsync(ctx, id, $"Hủy quyết toán chiết khấu: {reason.Trim()}");
            }
            run.VoidedAt = clock.UtcNow; run.VoidedBy = ctx.UserId;
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.void_run", "discount_agreement", agreementId.ToString(), new { run.Amount, reason = reason.Trim() });
            await db.SaveChangesAsync();
        });
    }
}
