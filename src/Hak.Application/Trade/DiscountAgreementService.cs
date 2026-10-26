using System.Text.Json;
using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record AgreementTierInput(decimal Threshold, decimal Percent);

public sealed record AgreementInput(InvoiceKind Kind, Guid PartnerId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AgreementTierInput> Tiers, bool Progressive = false);

public sealed record AgreementView(Guid Id, InvoiceKind Kind, Guid PartnerId, string PartnerCode, string PartnerName, string Code, string Name, DateOnly FromDate, DateOnly ToDate,
    bool IsActive, long RowVersion, IReadOnlyList<AgreementTierInput> Tiers, Guid? RunId, decimal? RunAmount, bool Progressive = false);

public sealed record AgreementInvoiceShare(Guid InvoiceId, string Number, string InvoiceSeries, string InvoiceNo, DateOnly PostingDate, decimal Sales, decimal Discount);

public sealed record AgreementStatementRow(int No, string OriginalSeries, string OriginalNo, DateOnly OriginalDate, decimal Sales, decimal Discount, string? AdjustmentSeries, string? AdjustmentNo, DateOnly? AdjustmentDate, decimal AdjustmentVat);

public sealed record AgreementStatement(string AgreementCode, string AgreementName, string PartnerCode, string PartnerName, DateOnly FromDate, DateOnly ToDate, decimal Percent, decimal TotalSales, decimal TotalDiscount,
    bool Settled, string Mode, IReadOnlyList<AgreementStatementRow> Rows, bool SnapshotAvailable = true, string? Warning = null);

public sealed record AgreementPreview(Guid AgreementId, decimal EligibleSales, decimal Percent, decimal Amount, decimal? NextThreshold, decimal? NextPercent, bool Settled,
    IReadOnlyList<AgreementInvoiceShare> Invoices, decimal Accrued = 0, IReadOnlyList<AgreementOverlap>? Overlaps = null);

/// <summary>Thỏa thuận khác còn áp dụng cùng chiều / đối tượng có khoảng ngày giao nhau: cùng nguồn doanh số có thể được hưởng nhiều chương trình. Chỉ để cảnh báo, không cấm (nhiều chương trình có thể được cộng dồn).</summary>
public sealed record AgreementOverlap(Guid AgreementId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, bool Settled, decimal SettledAmount);

/// <summary>
/// Chiết khấu thương mại theo doanh số cuối kỳ (rebate): thỏa thuận có bậc, tính trên doanh số hàng chưa thuế (tiền hạch toán) của hóa đơn thường đã ghi sổ trong khoảng ngày, trừ hàng trả lại / giảm giá đã ghi sổ.
/// Hóa đơn ngoại tệ tính doanh số theo tiền hạch toán nhưng chiết khấu từng dòng theo tiền của hóa đơn (và tỷ giá của chính nó). Quyết toán sinh hóa đơn điều chỉnh giảm NHÁP (loại chiết khấu thương mại) cho từng hóa đơn gốc, chia theo tỷ lệ doanh số và theo từng dòng
/// để thuế GTGT giảm đúng thuế suất của dòng; ghi sổ hóa đơn do người có quyền ghi sổ làm như mọi điều chỉnh khác (bên bán: Nợ chiết khấu thương mại; bên mua: giảm hàng tồn kho / giá vốn).
/// Mỗi thỏa thuận có một quyết toán gốc hiệu lực; bổ sung/thu hồi nối chuỗi có snapshot riêng. Lập / sửa điều khoản cần quyền duyệt chứng từ.
/// </summary>
public sealed partial class DiscountAgreementService(HakDbContext db, InvoiceService invoices, AuditService audit, IClock clock, AccountingEngine? engine = null)
{
    private static string Norm(string s) => s.Trim();

    private sealed record SourceLineAllocation(int LineNo, decimal Amount, decimal? VatRate, decimal OriginalFunctionalAmount);
    private sealed record SourceAllocation(Guid InvoiceId, string? Currency, decimal? ExchangeRate, int DecimalPlaces, IReadOnlyList<SourceLineAllocation> Lines, InvoicePurpose Purpose = InvoicePurpose.Discount);
    private sealed record FrozenAccountingLine(Guid AccountId, Side Side, decimal Amount, Guid? PartnerId, Guid? SettleJournalLineId = null);
    private sealed record SettlementSnapshot(int Version, int FunctionalDecimalPlaces, AgreementStatement Statement, AgreementPreview Preview, AgreementInput Policy, IReadOnlyList<SourceAllocation> Sources,
        DateOnly? VoucherPostingDate = null, IReadOnlyList<FrozenAccountingLine>? AccountingLines = null, IReadOnlyList<SourceAllocation>? TargetSources = null);

    private async Task CaptureSnapshotAsync(SessionContext ctx, DiscountAgreement a, Plan plan, DiscountAgreementRun run)
    {
        var statement = await BuildStatementAsync(a, plan, run);
        var preview = new AgreementPreview(a.Id, run.EligibleSales, run.Percent, run.Amount, null, null, true,
            plan.Shares.Select(s => new AgreementInvoiceShare(s.Invoice.Id, s.Invoice.Number, s.Invoice.InvoiceSeries, s.Invoice.InvoiceNo, s.Invoice.PostingDate, s.Sales, s.Discount)).ToList(), run.AccruedAmount);
        var policy = new AgreementInput(a.Kind, a.PartnerId, a.Code, a.Name, a.FromDate, a.ToDate,
            a.Tiers.OrderBy(t => t.Threshold).Select(t => new AgreementTierInput(t.Threshold, t.Percent)).ToList(), a.Progressive);
        var dp = await FunctionalDpAsync(ctx.CompanyId);
        var currencyDp = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.DecimalPlaces);
        var voucher = run.VoucherId is { } voucherId ? await db.Vouchers.AsNoTracking().Include(v => v.Lines).FirstAsync(v => v.Id == voucherId) : null;
        run.SettlementSnapshot = JsonSerializer.Serialize(new SettlementSnapshot(2, dp, statement, preview, policy,
            plan.Shares.Select(s => new SourceAllocation(s.Invoice.Id, s.Invoice.Currency, s.Invoice.ExchangeRate,
                s.Invoice.Currency is { } currency ? currencyDp[currency] : dp,
                s.Lines.Select(l => new SourceLineAllocation(l.LineNo, l.Amount,
                    s.Invoice.Lines.Single(x => x.LineNo == l.LineNo).VatRate, s.Invoice.Lines.Single(x => x.LineNo == l.LineNo).FunctionalAmount)).ToList())).ToList(),
            voucher?.PostingDate, voucher?.Lines.Select(l => new FrozenAccountingLine(l.AccountId, l.Side, l.Amount, l.PartnerId, l.SettleJournalLineId)).ToList()));
    }

    private static SettlementSnapshot? ReadSnapshot(DiscountAgreementRun run)
        => run.SettlementSnapshot is null ? null : JsonSerializer.Deserialize<SettlementSnapshot>(run.SettlementSnapshot)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Bảng kê quyết toán đã lưu không hợp lệ; kiểm tra dữ liệu trước khi xuất.");

    public async Task<IReadOnlyList<AgreementView>> ListAsync(SessionContext ctx, InvoiceKind kind)
    {
        ctx.Require(Permissions.ReportView);
        var list = await db.DiscountAgreements.AsNoTracking().Include(a => a.Tiers).Where(a => a.CompanyId == ctx.CompanyId && a.Kind == kind).OrderByDescending(a => a.FromDate).ThenBy(a => a.Code).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var runs = await db.DiscountAgreementRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId && r.VoidedAt == null && r.ParentRunId == null).ToDictionaryAsync(r => r.AgreementId);
        return list.Select(a => new AgreementView(a.Id, a.Kind, a.PartnerId, partners[a.PartnerId].Code, partners[a.PartnerId].Name, a.Code, a.Name, a.FromDate, a.ToDate, a.IsActive, a.RowVersion,
            a.Tiers.OrderBy(t => t.Threshold).Select(t => new AgreementTierInput(t.Threshold, t.Percent)).ToList(), runs.GetValueOrDefault(a.Id)?.Id, runs.GetValueOrDefault(a.Id)?.Amount, a.Progressive)).ToList();
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
                Id = Ids.New(), CompanyId = ctx.CompanyId, Kind = input.Kind, PartnerId = input.PartnerId, Code = code, Name = Norm(input.Name), FromDate = input.FromDate, ToDate = input.ToDate, Progressive = input.Progressive,
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
            a.Code = code; a.Name = Norm(input.Name); a.FromDate = input.FromDate; a.ToDate = input.ToDate; a.Progressive = input.Progressive;
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
    private async Task<Plan> PlanAsync(SessionContext ctx, DiscountAgreement a, DateOnly? upTo = null)
    {
        var dp = await FunctionalDpAsync(ctx.CompanyId);
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == a.Kind && i.PartnerId == a.PartnerId && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted
                        && i.PostingDate >= a.FromDate && i.PostingDate <= (upTo ?? a.ToDate))
            .OrderBy(i => i.PostingDate).ThenBy(i => i.Number).ToListAsync();
        var ids = invs.Select(i => i.Id).ToList();
        // Điều chỉnh giảm / trả hàng đã có (bỏ các hóa đơn thuộc lần quyết toán còn hiệu lực của chính thỏa thuận này để xem lại số liệu sau khi quyết toán vẫn ổn định).
        var own = await db.DiscountAgreementRunInvoices.AsNoTracking().Where(r => db.DiscountAgreementRuns.Any(x => x.Id == r.RunId && x.AgreementId == a.Id && x.VoidedAt == null)).Select(r => r.AdjustmentInvoiceId)
            .Concat(db.DiscountSettlementInvoiceLinks.Where(r => db.DiscountAgreementRuns.Any(x => x.Id == r.RunId && x.AgreementId == a.Id && x.VoidedAt == null)).Select(r => r.AdjustmentInvoiceId)).ToListAsync();
        var adj = await (from l in db.CommercialInvoiceLines.AsNoTracking()
                         join i in db.CommercialInvoices.AsNoTracking() on l.InvoiceId equals i.Id
                         where i.CompanyId == ctx.CompanyId && i.AdjustsInvoiceId != null && ids.Contains(i.AdjustsInvoiceId.Value) && !own.Contains(i.Id)
                               && (i.Purpose == InvoicePurpose.Return || i.Purpose == InvoicePurpose.Discount
                                   // Tăng giá / sửa sai doanh thu đã ghi sổ làm tăng doanh số tính CKTM; "tăng" lý do thu hồi CKTM (CommercialRebate) thì KHÔNG, tránh vòng tự sinh quyền lợi.
                                   || (i.Purpose == InvoicePurpose.Increase && i.AdjustmentReason != InvoiceAdjustmentReason.CommercialRebate))
                               && (i.Status == InvoiceStatus.Posted || i.Status == InvoiceStatus.Draft)
                         select new { Orig = i.AdjustsInvoiceId!.Value, l.AdjustsLineNo, i.Status, i.Purpose, l.FunctionalAmount, l.Amount }).ToListAsync();
        var adjByLine = adj.ToLookup(x => (x.Orig, x.AdjustsLineNo));

        // net = doanh số tính chiết khấu (chỉ điều chỉnh đã ghi sổ); room = chỗ còn giảm được trên dòng (tính cả điều chỉnh nháp). Hóa đơn ngoại tệ có thêm số theo tiền hóa đơn.
        var perInvoice = new List<(CommercialInvoice Inv, List<(int LineNo, decimal Net, decimal Room, decimal NetFx, decimal RoomFx)> Lines)>();
        foreach (var i in invs)
        {
            var lines = i.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var mine = adjByLine[(i.Id, (int?)l.LineNo)].ToList();
                // Tăng đã ghi sổ cộng vào doanh số và vào chỗ còn giảm được; giảm / trả hàng trừ (Net chỉ tính đã ghi sổ, Room tính cả nháp). Tăng còn nháp chưa làm tăng gì.
                var posted = mine.Where(x => x.Status == InvoiceStatus.Posted).ToList();
                var upF = posted.Where(x => x.Purpose == InvoicePurpose.Increase).Sum(x => x.FunctionalAmount); var upA = posted.Where(x => x.Purpose == InvoicePurpose.Increase).Sum(x => x.Amount);
                var downPostedF = posted.Where(x => x.Purpose != InvoicePurpose.Increase).Sum(x => x.FunctionalAmount); var downPostedA = posted.Where(x => x.Purpose != InvoicePurpose.Increase).Sum(x => x.Amount);
                var downAllF = mine.Where(x => x.Purpose != InvoicePurpose.Increase).Sum(x => x.FunctionalAmount); var downAllA = mine.Where(x => x.Purpose != InvoicePurpose.Increase).Sum(x => x.Amount);
                return (l.LineNo, Net: Math.Max(0, l.FunctionalAmount + upF - downPostedF), Room: Math.Max(0, l.FunctionalAmount + upF - downAllF),
                        NetFx: Math.Max(0, l.Amount + upA - downPostedA), RoomFx: Math.Max(0, l.Amount + upA - downAllA));
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
        decimal domesticAmount = 0;
        if (a.Progressive && perInvoice.Any(p => p.Inv.Currency != null && p.Lines.Sum(l => l.Net) > 0))
            throw new DomainException(ErrorCodes.ValidationFailed, $"Thỏa thuận {a.Code} tính lũy tiến từng phần nhưng có hóa đơn ngoại tệ trong kỳ; dùng cách hồi tố hoặc tách riêng hóa đơn ngoại tệ.");
        // Lũy tiến: mỗi phần doanh số nằm trong bậc [ngưỡng k; ngưỡng k+1) nhận tỷ lệ của bậc k (phần dưới ngưỡng thấp nhất không được giảm).
        if (a.Progressive)
        {
            decimal raw = 0;
            for (var k = 0; k < tiers.Count; k++)
            {
                var upper = k + 1 < tiers.Count ? tiers[k + 1].Threshold : decimal.MaxValue;
                var band = Math.Min(domesticSales, upper) - tiers[k].Threshold;
                if (band > 0) raw += band * tiers[k].Percent / 100m;
            }
            var progressive = MoneyMath.Round(raw, dp);
            percent = domesticSales > 0 ? MoneyMath.Round(progressive / domesticSales * 100m, 4) : 0;   // tỷ lệ BÌNH QUÂN thực tế, chỉ để hiển thị / mô tả
            domesticAmount = progressive;
        }
        var amount = a.Progressive ? domesticAmount : MoneyMath.Round(domesticSales * percent / 100m, dp);
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
        var currencyDp = new Dictionary<string, int>(StringComparer.Ordinal);
        // Hóa đơn ngoại tệ: chiết khấu từng dòng = % × tiền hàng dòng THEO TIỀN HÓA ĐƠN (tính tròn theo số lẻ của đồng tiền đó), quy ra tiền hạch toán theo tỷ giá của chính hóa đơn.
        foreach (var (inv, lines) in perInvoice.Where(p => p.Inv.Currency != null && p.Lines.Sum(l => l.Net) > 0))
        {
            if (!currencyDp.TryGetValue(inv.Currency!, out var curDp))
                currencyDp[inv.Currency!] = curDp = await db.Currencies.Where(c => c.Code == inv.Currency).Select(c => c.DecimalPlaces).FirstAsync();   // một lần cho mỗi loại tiền
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
        var run = await db.DiscountAgreementRuns.AsNoTracking().FirstOrDefaultAsync(r => r.AgreementId == id && r.VoidedAt == null && r.ParentRunId == null);
        if (run is not null)
            return ReadSnapshot(run)?.Preview ?? new AgreementPreview(id, run.EligibleSales, run.Percent, run.Amount, null, null, true, [], run.AccruedAmount);
        var plan = await PlanAsync(ctx, a);
        var settled = false;
        // Cảnh báo chồng kỳ: thỏa thuận khác còn áp dụng của cùng đối tượng, khoảng ngày giao nhau; kèm số đã quyết toán (nếu có) để người xem thấy tổng quyền lợi trên cùng nguồn doanh số.
        var others = await db.DiscountAgreements.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId && o.Id != a.Id && o.Kind == a.Kind && o.PartnerId == a.PartnerId && o.IsActive
                                                                            && o.FromDate <= a.ToDate && a.FromDate <= o.ToDate).OrderBy(o => o.FromDate).ThenBy(o => o.Code).ToListAsync();
        var otherIds = others.Select(o => o.Id).ToList();
        var runs = await db.DiscountAgreementRuns.AsNoTracking().Where(r => otherIds.Contains(r.AgreementId) && r.VoidedAt == null).Select(r => new { r.AgreementId, r.Amount }).ToListAsync();
        var overlaps = others.Select(o => new AgreementOverlap(o.Id, o.Code, o.Name, o.FromDate, o.ToDate, runs.Any(r => r.AgreementId == o.Id), runs.Where(r => r.AgreementId == o.Id).Sum(r => r.Amount))).ToList();
        return new AgreementPreview(id, plan.Eligible, plan.Percent, plan.Amount, plan.NextThreshold, plan.NextPercent, settled,
            plan.Shares.Select(s => new AgreementInvoiceShare(s.Invoice.Id, s.Invoice.Number, s.Invoice.InvoiceSeries, s.Invoice.InvoiceNo, s.Invoice.PostingDate, s.Sales, s.Discount)).ToList(),
            await PostedAccrualAsync(id), overlaps.Count == 0 ? null : overlaps);
    }

    /// <summary>
    /// Bảng kê các hóa đơn gốc được chiết khấu (dùng đính kèm hóa đơn điều chỉnh / chứng từ điều chỉnh giảm — Mẫu 01/BK-ĐCTT theo cách gọi trong Thông tư 91/2026 [CẦN XÁC MINH các cột của mẫu chính thức]).
    /// Mỗi dòng: hóa đơn gốc (ký hiệu, số, ngày), doanh số chưa thuế, số chiết khấu chia cho hóa đơn đó và — nếu đã quyết toán bằng hóa đơn điều chỉnh — hóa đơn điều chỉnh tương ứng với tiền thuế điều chỉnh.
    /// </summary>
    public async Task<AgreementStatement> StatementAsync(SessionContext ctx, Guid id, Guid? runId = null)
    {
        ctx.Require(Permissions.ReportView);
        var a = await LoadAsync(ctx, id);
        var run = await db.DiscountAgreementRuns.AsNoTracking().Include(r => r.Invoices).FirstOrDefaultAsync(r => r.AgreementId == id && r.CompanyId == ctx.CompanyId && (runId != null ? r.Id == runId : r.ParentRunId == null && r.VoidedAt == null));
        if (runId != null && run is null) throw new DomainException(ErrorCodes.NotFound, "Không có lần quyết toán được chọn.");
        if (run is not null)
        {
            var snapshot = ReadSnapshot(run);
            if (snapshot is not null)
            {
                // Provider-assigned legal identities can arrive after settlement. Financial figures remain frozen.
                var adjIds = run.Invoices.Select(i => i.AdjustmentInvoiceId).ToList();
                var identities = await db.CommercialInvoices.AsNoTracking().Where(i => adjIds.Contains(i.Id) && i.CompanyId == ctx.CompanyId)
                    .ToListAsync();
                var sourceIds = snapshot.Preview.Invoices.GroupBy(i => (i.InvoiceSeries, i.InvoiceNo)).ToDictionary(g => g.Key, g => g.First().InvoiceId);
                return snapshot.Statement with { Rows = snapshot.Statement.Rows.Select(r =>
                    sourceIds.TryGetValue((r.OriginalSeries, r.OriginalNo), out var sourceId) && identities.FirstOrDefault(i => i.AdjustsInvoiceId == sourceId && i.Purpose == (r.Discount < 0 ? InvoicePurpose.Increase : InvoicePurpose.Discount)) is { } adj
                        ? r with { AdjustmentSeries = adj.InvoiceSeries, AdjustmentNo = adj.InvoiceNo, AdjustmentDate = adj.InvoiceDate } : r).ToList() };
            }
            var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == a.PartnerId);
            return new AgreementStatement(a.Code, a.Name, partner.Code, partner.Name, a.FromDate, a.ToDate, run.Percent, run.EligibleSales, run.Amount, true,
                run.VoucherId is null ? "Hóa đơn điều chỉnh" : "Hạch toán điều chỉnh giảm", [], false,
                "Quyết toán cũ chưa lưu bảng kê nguồn. Tổng tiền lấy từ lần quyết toán; cần đối chiếu hồ sơ cũ để bổ sung chi tiết, không tính lại từ hóa đơn hiện tại.");
        }
        return await BuildStatementAsync(a, await PlanAsync(ctx, a), null);
    }

    private async Task<AgreementStatement> BuildStatementAsync(DiscountAgreement a, Plan plan, DiscountAgreementRun? run)
    {
        var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == a.PartnerId);
        var adjByOriginal = new Dictionary<Guid, (string Series, string No, DateOnly Date, decimal Vat)>();
        if (run is not null && run.VoucherId is null)
        {
            var allocations = await db.DiscountAgreementRunInvoices.AsNoTracking().Where(i => i.RunId == run.Id).ToListAsync();
            var ids = allocations.Select(i => i.AdjustmentInvoiceId).ToList();
            var adjs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id)).ToListAsync();
            foreach (var ri in allocations)
            {
                var adj = adjs.FirstOrDefault(x => x.Id == ri.AdjustmentInvoiceId);
                if (adj is not null) adjByOriginal[ri.OriginalInvoiceId] = (adj.InvoiceSeries, adj.InvoiceNo, adj.InvoiceDate, adj.Lines.Sum(l => l.VatAmount));
            }
        }
        // Sau quyết toán số liệu lấy từ lần quyết toán (ổn định); trước đó lấy theo tính toán hiện tại.
        var rows = plan.Shares.Where(s => s.Discount > 0).Select((s, i) =>
        {
            adjByOriginal.TryGetValue(s.Invoice.Id, out var adj);
            return new AgreementStatementRow(i + 1, s.Invoice.InvoiceSeries, s.Invoice.InvoiceNo, s.Invoice.InvoiceDate, s.Sales, s.Discount, adj.Series, adj.No, adj.Series is null ? null : adj.Date, adj.Vat);
        }).ToList();
        var mode = run is null ? "Chưa quyết toán" : run.VoucherId is not null ? "Hạch toán điều chỉnh giảm" : "Hóa đơn điều chỉnh";
        return new AgreementStatement(a.Code, a.Name, partner.Code, partner.Name, a.FromDate, a.ToDate, plan.Percent, rows.Sum(r => r.Sales), rows.Sum(r => r.Discount), run is not null, mode, rows);
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
            var accruedAmount = await SettledAccrualAsync(a.Id);
            run.AccruedAmount = accruedAmount;
            run.AccrualSettlementMode = DiscountAccrualSettlementMode.PerInvoice;
            if (accruedAmount > 0)
            {
                var accounts = await RebateAccountsAsync(ctx.CompanyId, a.Kind, true, false);
                await VerifyAccrualAccountsAsync(a, accounts.Discount, accounts.Balance!.Value);
                run.RecognitionAccountId = accounts.Discount;
                run.AccrualAccountId = accounts.Balance;
            }
            var shares = plan.Shares.Where(s => s.Lines.Count > 0).ToList();
            decimal accruedLeft = accruedAmount;
            var dp = await FunctionalDpAsync(ctx.CompanyId);
            var n = 0;
            foreach (var share in shares)
            {
                var inv = await invoices.CreateAdjustmentAsync(ctx, new AdjustmentInput(share.Invoice.Id, InvoicePurpose.Discount, invoiceSeries.Trim(), InvoiceNoAt(firstInvoiceNo.Trim(), n++), invoiceDate, postingDate,
                    $"Chiết khấu thương mại theo thỏa thuận {a.Code} ({a.FromDate.Vn()} – {a.ToDate.Vn()}), đạt {plan.Percent}%",
                    share.Lines.Select(l => new AdjustmentLineInput(l.LineNo, Amount: l.Amount, Description: $"Chiết khấu thương mại {plan.Percent}% – {a.Name}")).ToList(),
                    DiscountKind.Trade, UnderAgreement: true));
                var accruedShare = n == shares.Count ? accruedLeft : Math.Min(accruedLeft, MoneyMath.Round(accruedAmount * share.Discount / plan.Amount, dp));
                accruedLeft -= accruedShare;
                db.DiscountAgreementRunInvoices.Add(new DiscountAgreementRunInvoice { Id = Ids.New(), RunId = run.Id, CompanyId = ctx.CompanyId, OriginalInvoiceId = share.Invoice.Id, AdjustmentInvoiceId = inv.Id, Amount = share.Discount, AccruedAmount = accruedShare });
            }
            await db.SaveChangesAsync();
            // Số thật của các hóa đơn điều chỉnh vừa tạo (tiền hạch toán) phải đúng bằng số đã tính; lệch nghĩa là có dòng đã giảm gần hết.
            var createdIds = await db.DiscountAgreementRunInvoices.Where(r => r.RunId == run.Id).Select(r => r.AdjustmentInvoiceId).ToListAsync();
            var actual = (await db.CommercialInvoiceLines.AsNoTracking().Where(l => createdIds.Contains(l.InvoiceId)).Select(l => l.FunctionalAmount).ToListAsync()).Sum();
            var drift = plan.Amount - actual;
            if (drift != 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Không chia hết số chiết khấu {plan.Amount.Vn(0)} lên các dòng hóa đơn (lệch {drift.Vn(0)}: dòng đã giảm gần hết); kiểm tra các điều chỉnh nháp đang có.");
            await CaptureSnapshotAsync(ctx, a, plan, run);
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.settle", "discount_agreement", a.Id.ToString(), new { a.Code, run.EligibleSales, run.Percent, run.Amount, invoices = n });
            await db.SaveChangesAsync();
            return run;
        });
    }

    public const string VoucherSource = "HAK.TRADEDISC";
    public const string AccrualSource = "HAK.TRADEDISC.ACCRUAL";

    /// <summary>
    /// Tài khoản hạch toán rebate theo chiều. Bán: giảm trừ doanh thu (Nợ, SALES_TRADE_DISCOUNT hoặc SALES_DISCOUNT) / phải trả chiết khấu (Có, REBATE_PAYABLE ~ 338) / phải thu khách (AR_TRADE).
    /// Mua: giảm giá vốn hàng mua (Có, PURCHASE_TRADE_DISCOUNT) / phải thu chiết khấu của NCC (Nợ, REBATE_RECEIVABLE ~ 138) / phải trả NCC (AP_TRADE). Chiều mua ghi vào TK giảm giá vốn,
    /// KHÔNG định giá lại tồn kho — nếu muốn giảm giá trị tồn kho thì quyết toán bằng hóa đơn điều chỉnh [CẦN XÁC MINH cách phân bổ vào tồn kho / giá vốn].
    /// </summary>
    private sealed record RebateAccounts(Guid Discount, Guid? Balance, Guid? OpenItem, Side DSide, Side BSide);

    private async Task<RebateAccounts> RebateAccountsAsync(Guid companyId, InvoiceKind kind, bool needBalance, bool needOpenItem)
    {
        var sales = kind == InvoiceKind.Sales;
        var codes = sales ? new[] { "SALES_TRADE_DISCOUNT", "SALES_DISCOUNT", "REBATE_PAYABLE", "AR_TRADE" } : new[] { "PURCHASE_TRADE_DISCOUNT", "REBATE_RECEIVABLE", "AP_TRADE" };
        var roles = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == companyId && codes.Contains(m.RoleCode)).ToDictionaryAsync(m => m.RoleCode, m => m.AccountId);
        Guid Need(string code, string what) => roles.TryGetValue(code, out var id) ? id : throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {code} ({what}).");
        var discount = sales ? (roles.TryGetValue("SALES_TRADE_DISCOUNT", out var t) ? t : Need("SALES_DISCOUNT", "chiết khấu thương mại / giảm giá hàng bán")) : Need("PURCHASE_TRADE_DISCOUNT", "chiết khấu thương mại được hưởng, giảm giá vốn hàng mua");
        Guid? balance = needBalance ? Need(sales ? "REBATE_PAYABLE" : "REBATE_RECEIVABLE", sales ? "phải trả chiết khấu đã trích trước" : "phải thu chiết khấu của nhà cung cấp đã trích trước") : null;
        Guid? open = needOpenItem ? Need(sales ? "AR_TRADE" : "AP_TRADE", sales ? "phải thu khách hàng" : "phải trả nhà cung cấp") : null;
        return new(discount, balance, open, sales ? Side.Debit : Side.Credit, sales ? Side.Credit : Side.Debit);
    }
    private static Side Opp(Side s) => s == Side.Debit ? Side.Credit : Side.Debit;

    /// <summary>Tổng đã trích trước của chứng từ ĐÃ GHI SỔ.</summary>
    private async Task<decimal> PostedAccrualAsync(Guid agreementId)
        => (await (from x in db.DiscountAccruals.AsNoTracking() join v in db.Vouchers.AsNoTracking() on x.VoucherId equals v.Id
                   where x.AgreementId == agreementId && v.Status == VoucherStatus.Posted select x.Adjustment).ToListAsync()).Sum();

    /// <summary>Số đã trích trước dùng khi quyết toán; còn chứng từ trích trước chưa ghi sổ thì từ chối (hủy hoặc ghi sổ trước).</summary>
    private async Task<decimal> SettledAccrualAsync(Guid agreementId)
    {
        var pending = await (from x in db.DiscountAccruals.AsNoTracking() join v in db.Vouchers.AsNoTracking() on x.VoucherId equals v.Id
                             where x.AgreementId == agreementId && v.Status != VoucherStatus.Posted && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed select v.Number).ToListAsync();
        if (pending.Count > 0) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Còn chứng từ trích trước chưa ghi sổ ({string.Join(", ", pending)}); ghi sổ hoặc hủy trước khi quyết toán.");
        return await PostedAccrualAsync(agreementId);
    }

    /// <summary>
    /// Trích trước chiết khấu theo doanh số đến <paramref name="asOf"/> (chiều bán): ước tính theo bậc đang đạt, sinh chứng từ NHÁP điều chỉnh phần chênh so với số đã trích (Nợ giảm trừ doanh thu /
    /// Có phải trả chiết khấu; chênh âm là hoàn nhập). Ở bước trích trước không điều chỉnh thuế GTGT [CẦN XÁC MINH]. Quyết toán sau đó bù trừ số đã trích để không giảm doanh thu hai lần.
    /// </summary>
    public async Task<DiscountAccrual> AccrueAsync(SessionContext ctx, Guid id, long expectedRowVersion, DateOnly asOf)
    {
        ctx.Require(Permissions.VoucherCreate);
        if (engine is null) throw new InvalidOperationException("Thiếu AccountingEngine.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            RowVersionCheck.Require(a.RowVersion, expectedRowVersion, $"Thỏa thuận {a.Code}");
            if (!a.IsActive) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thỏa thuận {a.Code} đã ngừng áp dụng.");
            if (asOf < a.FromDate || asOf > a.ToDate) throw new DomainException(ErrorCodes.ValidationFailed, $"Ngày trích trước phải trong kỳ thỏa thuận ({a.FromDate.Vn()} – {a.ToDate.Vn()}).");
            if (await db.DiscountAgreementRuns.AnyAsync(r => r.AgreementId == id && r.VoidedAt == null)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thỏa thuận {a.Code} đã quyết toán.");
            var accrued = await SettledAccrualAsync(id);                       // chặn khi còn chứng từ trích trước nháp
            var plan = await PlanAsync(ctx, a, asOf);
            var adjustment = plan.Amount - accrued;
            if (adjustment == 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Số ước tính {plan.Amount.Vn(0)} bằng số đã trích trước; không có gì để điều chỉnh.");
            var ra = await RebateAccountsAsync(ctx.CompanyId, a.Kind, true, false);
            var desc = $"{(adjustment > 0 ? "Trích trước" : "Hoàn nhập trích trước")} chiết khấu thương mại theo thỏa thuận {a.Code} đến {asOf.Vn()} (doanh số {plan.Eligible.Vn(0)}, {plan.Percent}%)";
            var amt = Math.Abs(adjustment);
            // Tăng ước tính: bên giảm trừ (dSide) ghi tăng, TK số dư (phải trả / phải thu) ghi ngược lại; hoàn nhập: đảo.
            var lines = adjustment > 0
                ? new List<VoucherLineInput> { new(ra.Discount, ra.DSide, amt, Description: desc), new(ra.Balance!.Value, ra.BSide, amt, a.PartnerId, Description: desc) }
                : new List<VoucherLineInput> { new(ra.Balance!.Value, Opp(ra.BSide), amt, a.PartnerId, Description: desc), new(ra.Discount, Opp(ra.DSide), amt, Description: desc) };
            var row = new DiscountAccrual { Id = Ids.New(), CompanyId = ctx.CompanyId, AgreementId = id, AsOf = asOf, Estimated = plan.Amount, Adjustment = adjustment, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, asOf, asOf, desc, lines, SourceSystem: AccrualSource, SourceId: row.Id.ToString()));
            row.VoucherId = v.Id;
            db.DiscountAccruals.Add(row);
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.accrue", "discount_agreement", a.Id.ToString(), new { a.Code, asOf, row.Estimated, adjustment, voucher = v.Number });
            await db.SaveChangesAsync();
            return row;
        });
    }

    /// <summary>
    /// Quyết toán bằng HẠCH TOÁN ĐIỀU CHỈNH GIẢM (thay cho hóa đơn điều chỉnh — người dùng chọn một trong hai): một chứng từ nháp Nợ giảm trừ doanh thu (SALES_TRADE_DISCOUNT, chưa ánh xạ thì
    /// SALES_DISCOUNT) / Có phải thu của đối tượng, không đụng hóa đơn gốc và không đổi thuế GTGT [CẦN XÁC MINH cách xử lý thuế khi không có hóa đơn điều chỉnh]. Chỉ chiều bán, hóa đơn tiền hạch toán.
    /// </summary>
    public async Task<DiscountAgreementRun> SettleByVoucherAsync(SessionContext ctx, Guid id, long expectedRowVersion, DateOnly postingDate)
    {
        ctx.Require(Permissions.VoucherCreate);
        if (engine is null) throw new InvalidOperationException("Thiếu AccountingEngine.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            RowVersionCheck.Require(a.RowVersion, expectedRowVersion, $"Thỏa thuận {a.Code}");
            if (!a.IsActive) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thỏa thuận {a.Code} đã ngừng áp dụng.");
            if (postingDate < a.ToDate) throw new DomainException(ErrorCodes.ValidationFailed, $"Chỉ quyết toán sau khi hết kỳ thỏa thuận ({a.ToDate.Vn()}).");
            if (await db.DiscountAgreementRuns.AnyAsync(r => r.AgreementId == id && r.VoidedAt == null))
                throw new DomainException(ErrorCodes.DuplicateSource, $"Thỏa thuận {a.Code} đã quyết toán; hủy lần quyết toán cũ nếu cần làm lại.");
            var plan = await PlanAsync(ctx, a);
            if (plan.Shares.Any(s => s.Invoice.Currency != null)) throw new DomainException(ErrorCodes.ValidationFailed, "Thỏa thuận có hóa đơn ngoại tệ: quyết toán bằng hóa đơn điều chỉnh (hạch toán điều chỉnh giảm chỉ dùng cho hóa đơn tiền hạch toán).");
            if (plan.Amount <= 0) throw new DomainException(ErrorCodes.ValidationFailed, plan.Percent == 0
                ? $"Doanh số {plan.Eligible.Vn(0)} chưa đạt bậc chiết khấu nào của thỏa thuận {a.Code}." : "Không có số chiết khấu phát sinh.");
            var accrued = await SettledAccrualAsync(a.Id);
            var ra = await RebateAccountsAsync(ctx.CompanyId, a.Kind, accrued > 0, true);
            if (accrued > 0) await VerifyAccrualAccountsAsync(a, ra.Discount, ra.Balance!.Value);
            var run = new DiscountAgreementRun { Id = Ids.New(), CompanyId = ctx.CompanyId, AgreementId = a.Id, EligibleSales = plan.Eligible, Percent = plan.Percent, Amount = plan.Amount, AccruedAmount = accrued, RecognitionAccountId = ra.Discount, AccrualAccountId = ra.Balance, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            var desc = $"Chiết khấu thương mại theo thỏa thuận {a.Code} ({a.FromDate.Vn()} – {a.ToDate.Vn()}), đạt {plan.Percent}% — hạch toán điều chỉnh giảm";
            // Có trích trước: bù trừ số đã trích (Nợ phải trả chiết khấu) rồi chỉ ghi phần chênh vào giảm trừ doanh thu (thêm hoặc hoàn nhập) — không giảm doanh thu hai lần.
            var lines = new List<VoucherLineInput>();
            if (accrued > 0)
            {
                lines.Add(new(ra.Balance!.Value, ra.DSide, accrued, a.PartnerId, Description: desc));                     // bù trừ số đã trích (xóa TK phải trả / phải thu chiết khấu)
                lines.Add(new(ra.OpenItem!.Value, ra.BSide, plan.Amount, a.PartnerId, Description: desc));                // giảm công nợ phải thu / phải trả
                var diff = plan.Amount - accrued;
                if (diff > 0) lines.Add(new(ra.Discount, ra.DSide, diff, Description: desc + " (phần chênh so với đã trích trước)"));
                else if (diff < 0) lines.Add(new(ra.Discount, Opp(ra.DSide), -diff, Description: desc + " (hoàn nhập phần trích trước thừa)"));
            }
            else
            {
                lines.Add(new(ra.Discount, ra.DSide, plan.Amount, Description: desc));
                lines.Add(new(ra.OpenItem!.Value, ra.BSide, plan.Amount, a.PartnerId, Description: desc));
            }
            lines.RemoveAll(l => l.AccountId == ra.OpenItem!.Value && l.PartnerId == a.PartnerId && l.Side == ra.BSide);
            lines.AddRange(await SettlementPartnerLinesAsync(ctx, plan, ra.OpenItem!.Value, ra.BSide, desc));
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, postingDate, postingDate, desc, lines, SourceSystem: VoucherSource, SourceId: run.Id.ToString()));
            run.VoucherId = v.Id;
            await CaptureSnapshotAsync(ctx, a, plan, run);
            db.DiscountAgreementRuns.Add(run);
            await audit.AppendAsync(ctx.CompanyId, ctx, "discount_agreement.settle", "discount_agreement", a.Id.ToString(), new { a.Code, run.EligibleSales, run.Percent, run.Amount, mode = "voucher", voucher = v.Number });
            await db.SaveChangesAsync();
            return run;
        });
    }

    /// <summary>Hủy run được chọn, mặc định run hiệu lực mới nhất. Hủy nháp; đã ghi sổ phải đảo trước; không hủy cha còn con hiệu lực.</summary>
    public async Task VoidRunAsync(SessionContext ctx, Guid agreementId, string reason, Guid? runId = null)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var run = await db.DiscountAgreementRuns.Include(r => r.Invoices).Where(r => r.AgreementId == agreementId && r.CompanyId == ctx.CompanyId && r.VoidedAt == null && (runId == null || r.Id == runId)).OrderByDescending(r => r.Sequence).FirstOrDefaultAsync()
                      ?? throw new DomainException(ErrorCodes.NotFound, "Thỏa thuận chưa quyết toán.");
            if (await db.DiscountAgreementRuns.AnyAsync(r => r.ParentRunId == run.Id && r.VoidedAt == null)) throw new DomainException(ErrorCodes.InvalidStateTransition, "Hủy lần bổ sung/thu hồi phía sau trước.");
            if (run.VoucherId is { } vid)
            {
                var vch = await db.Vouchers.AsNoTracking().FirstAsync(x => x.Id == vid);
                if (vch.Status is VoucherStatus.Posted) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đã ghi sổ chứng từ {vch.Number}; đảo chứng từ này trước khi hủy quyết toán.");
                if (vch.Status is not (VoucherStatus.Cancelled or VoucherStatus.Reversed)) await engine!.CancelAsync(ctx, vid, $"Hủy quyết toán chiết khấu: {reason.Trim()}");
            }
            if (run.AccrualReversalVoucherId is { } rvid)
            {
                var rv = await db.Vouchers.AsNoTracking().FirstAsync(x => x.Id == rvid);
                if (rv.Status is VoucherStatus.Posted) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đã ghi sổ chứng từ hoàn nhập trích trước {rv.Number}; đảo chứng từ này trước khi hủy quyết toán.");
                if (rv.Status is not (VoucherStatus.Cancelled or VoucherStatus.Reversed)) await engine!.CancelAsync(ctx, rvid, $"Hủy quyết toán chiết khấu: {reason.Trim()}");
            }
            var ids = run.Invoices.Select(i => i.AdjustmentInvoiceId).Concat(await db.DiscountSettlementInvoiceLinks.Where(l => l.RunId == run.Id).Select(l => l.AdjustmentInvoiceId).ToListAsync()).Distinct().ToList();
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
