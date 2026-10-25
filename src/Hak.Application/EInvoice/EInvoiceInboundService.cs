using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Tax;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.EInvoice;

public enum InboundSuggestionCode
{
    /// <summary>Nhà cung cấp lập hóa đơn điều chỉnh nhưng sổ chưa có hóa đơn điều chỉnh tương ứng.</summary>
    AdjustmentNotBooked = 1,
    /// <summary>Nhà cung cấp lập hóa đơn thay thế nhưng sổ chưa ghi hóa đơn thay thế.</summary>
    ReplacementNotBooked = 2,
    /// <summary>Hóa đơn thay thế đã ghi sổ nhưng hóa đơn gốc bị thay vẫn còn hiệu lực trong sổ.</summary>
    ReplacedOriginalStillActive = 3,
    /// <summary>Hóa đơn điều chỉnh / thay thế của nhà cung cấp tham chiếu hóa đơn gốc chưa có trong sổ.</summary>
    OriginalNotBooked = 4,
}

public sealed record InboundSuggestion(InboundSuggestionCode Code, Guid EInvoiceId, string Label, Guid? OriginalInvoiceId, string? OriginalNumber, decimal AmountBeforeVat, decimal VatAmount, string Message);

/// <summary>
/// Hóa đơn MUA: nhà cung cấp phát hành điều chỉnh / thay thế (nhận qua nhập tệp hoặc cổng nhà cung cấp, đã nằm trong dữ liệu HĐĐT đầu vào). Hệ thống KHÔNG tự ghi sổ mà liệt kê việc cần làm
/// và tạo sẵn hóa đơn điều chỉnh NHÁP (giảm giá / chiết khấu / điều chỉnh tăng) mang đúng ký hiệu và số của nhà cung cấp để người có thẩm quyền duyệt và ghi sổ (giảm tồn kho / giá vốn,
/// thuế đầu vào, công nợ theo quyết định đã chốt). Hàng trả lại cần phiếu xuất trả nên không tạo tự động. Thay thế: đảo hóa đơn gốc rồi ghi nhận hóa đơn mới bằng chức năng sẵn có.
/// </summary>
public sealed class EInvoiceInboundService(HakDbContext db, InvoiceService invoices, AuditService audit)
{
    private static string Norm(string? v) => (v ?? "").Replace(" ", "").Trim();

    public async Task<IReadOnlyList<InboundSuggestion>> SuggestionsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var recs = await LoadAsync(ctx.CompanyId);
        var books = await BooksAsync(ctx.CompanyId);
        var result = new List<InboundSuggestion>();
        foreach (var (rec, status) in recs.Where(r => r.Status is EInvoiceStatus.Adjustment or EInvoiceStatus.Replacement))
        {
            var label = $"{rec.SellerName} {rec.Series}/{rec.Number}";
            var original = books.FirstOrDefault(b => b.Seller == rec.SellerTaxCode && b.Series == rec.RelatedSeries && b.No == rec.RelatedNumber && b.Purpose == InvoicePurpose.Normal);
            var self = books.Where(b => b.Seller == rec.SellerTaxCode && b.Series == rec.Series && b.No == rec.Number).ToList();
            if (original is null && rec.RelatedSeries is not null)
            {
                if (self.Count == 0)
                    result.Add(new(InboundSuggestionCode.OriginalNotBooked, rec.Id, label, null, null, rec.AmountBeforeVat, rec.VatAmount,
                        $"{(status == EInvoiceStatus.Replacement ? "Hóa đơn thay thế" : "Hóa đơn điều chỉnh")} {rec.Series}/{rec.Number} tham chiếu hóa đơn gốc {rec.RelatedSeries}/{rec.RelatedNumber} chưa có trong sổ: ghi nhận hóa đơn gốc trước hoặc kiểm tra tham chiếu."));
                continue;
            }
            if (status == EInvoiceStatus.Adjustment)
            {
                if (self.Any(s => s.Purpose != InvoicePurpose.Normal)) continue;
                result.Add(new(InboundSuggestionCode.AdjustmentNotBooked, rec.Id, label, original?.InvoiceId, original?.Number, rec.AmountBeforeVat, rec.VatAmount,
                    $"Nhà cung cấp điều chỉnh hóa đơn {rec.RelatedSeries}/{rec.RelatedNumber}: {rec.AmountBeforeVat:N0} + thuế {rec.VatAmount:N0}. Tạo hóa đơn điều chỉnh nháp (giảm giá / chiết khấu hoặc tăng); hàng trả lại thì lập phiếu xuất trả rồi hóa đơn trả hàng."));
            }
            else
            {
                var replacementBooked = self.Any(s => s.Purpose == InvoicePurpose.Normal && s.Status == InvoiceStatus.Posted);
                if (!replacementBooked)
                    result.Add(new(InboundSuggestionCode.ReplacementNotBooked, rec.Id, label, original?.InvoiceId, original?.Number, rec.AmountBeforeVat, rec.VatAmount,
                        $"Hóa đơn {rec.RelatedSeries}/{rec.RelatedNumber} đã bị {rec.Series}/{rec.Number} thay thế: đảo hóa đơn gốc rồi ghi nhận hóa đơn thay thế."));
                else if (original?.Status == InvoiceStatus.Posted)
                    result.Add(new(InboundSuggestionCode.ReplacedOriginalStillActive, rec.Id, label, original.InvoiceId, original.Number, rec.AmountBeforeVat, rec.VatAmount,
                        $"Hóa đơn thay thế {rec.Series}/{rec.Number} đã ghi sổ nhưng hóa đơn gốc {original.Number} còn hiệu lực: đảo hóa đơn gốc để không tính hai lần."));
            }
        }
        // Hóa đơn gốc bị thay thế / hủy trên hệ thống HĐĐT (trạng thái mới nhất) mà sổ còn ghi: đã có trong đối soát ba nguồn (CancelledButRecorded); ở đây không lặp lại.
        return result.OrderBy(r => r.Code).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Tạo hóa đơn điều chỉnh NHÁP của hóa đơn mua từ bản ghi HĐĐT của nhà cung cấp. Số tiền chia cho các dòng hóa đơn gốc theo tỷ lệ giá trị dòng (dòng cuối nhận phần dư); thuế tính theo
/// thuế suất từng dòng phải khớp thuế trên hóa đơn của nhà cung cấp, nếu không thì dừng (nhập tay để kiểm soát).
    /// </summary>
    public async Task<CommercialInvoice> CreateAdjustmentDraftAsync(SessionContext ctx, Guid eInvoiceId, InvoicePurpose purpose, DiscountKind? discountKind, DateOnly postingDate)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (purpose is not (InvoicePurpose.Discount or InvoicePurpose.Increase))
            throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ tạo tự động hóa đơn giảm giá / chiết khấu hoặc điều chỉnh tăng; hàng trả lại cần phiếu xuất trả.");
        var rec = await db.EInvoiceRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == eInvoiceId && r.CompanyId == ctx.CompanyId && r.Direction == TaxDirection.Input)
                  ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn điện tử đầu vào không tồn tại.");
        if (rec.Status != EInvoiceStatus.Adjustment || rec.RelatedSeries is null) throw new DomainException(ErrorCodes.ValidationFailed, "Bản ghi này không phải hóa đơn điều chỉnh có tham chiếu hóa đơn gốc.");
        var books = await BooksAsync(ctx.CompanyId);
        var original = books.FirstOrDefault(b => b.Seller == rec.SellerTaxCode && b.Series == rec.RelatedSeries && b.No == rec.RelatedNumber && b.Purpose == InvoicePurpose.Normal && b.Status == InvoiceStatus.Posted)
                       ?? throw new DomainException(ErrorCodes.NotFound, $"Hóa đơn gốc {rec.RelatedSeries}/{rec.RelatedNumber} chưa ghi sổ hoặc không còn hiệu lực.");
        if (books.Any(b => b.Seller == rec.SellerTaxCode && b.Series == rec.Series && b.No == rec.Number && b.Status != InvoiceStatus.Cancelled))
            throw new DomainException(ErrorCodes.DuplicateSource, $"Hóa đơn {rec.Series}/{rec.Number} đã có trong sổ.");
        var lines = await db.CommercialInvoiceLines.AsNoTracking().Where(l => l.InvoiceId == original.InvoiceId).OrderBy(l => l.LineNo).ToListAsync();
        var total = lines.Sum(l => l.FunctionalAmount);
        if (total <= 0 || rec.AmountBeforeVat <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không chia được số tiền điều chỉnh.");
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
        var dp = await db.Currencies.Where(c => c.Code == company.FunctionalCurrency).Select(c => c.DecimalPlaces).FirstAsync();
        var shares = new List<AdjustmentLineInput>();
        var left = rec.AmountBeforeVat; decimal vat = 0;
        foreach (var l in lines)
        {
            var share = l == lines[^1] ? left : Math.Min(left, MoneyMath.Round(rec.AmountBeforeVat * l.FunctionalAmount / total, dp));
            left -= share;
            if (share <= 0) continue;
            shares.Add(new AdjustmentLineInput(l.LineNo, Amount: share, Description: $"Theo hóa đơn {rec.Series}/{rec.Number} của {rec.SellerName}"));
            vat += l.VatRate is { } r ? MoneyMath.Round(share * r / 100m, dp) : 0;
        }
        if (vat != rec.VatAmount)
            throw new DomainException(ErrorCodes.ValidationFailed, $"Thuế tính theo thuế suất dòng hóa đơn gốc ({vat:N0}) khác thuế trên hóa đơn của nhà cung cấp ({rec.VatAmount:N0}); lập hóa đơn điều chỉnh thủ công.");
        var draft = await invoices.CreateAdjustmentAsync(ctx, new AdjustmentInput(original.InvoiceId, purpose, rec.Series, rec.Number, rec.IssueDate, postingDate,
            $"{(purpose == InvoicePurpose.Increase ? "Điều chỉnh tăng" : "Điều chỉnh giảm")} theo hóa đơn {rec.Series}/{rec.Number} của {rec.SellerName}", shares, purpose == InvoicePurpose.Discount ? discountKind ?? DiscountKind.Allowance : null));
        await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.inbound.adjustment_draft", "invoice", draft.Id.ToString(), new { eInvoice = $"{rec.Series}/{rec.Number}", original = original.Number, purpose });
        await db.SaveChangesAsync();
        return draft;
    }

    private async Task<List<(EInvoiceRecord Rec, EInvoiceStatus Status)>> LoadAsync(Guid companyId)
    {
        var recs = await db.EInvoiceRecords.AsNoTracking().Where(r => r.CompanyId == companyId && r.Direction == TaxDirection.Input).ToListAsync();
        var latest = (await db.EInvoiceStatusChanges.AsNoTracking().Where(c => recs.Select(r => r.Id).Contains(c.EInvoiceId)).ToListAsync())
            .GroupBy(c => c.EInvoiceId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.At).Last().NewStatus);
        return recs.Select(r => (r, latest.TryGetValue(r.Id, out var s) ? s : r.Status)).ToList();
    }

    private sealed record Book(Guid InvoiceId, string Number, string Seller, string Series, string No, InvoicePurpose Purpose, InvoiceStatus Status);

    private async Task<List<Book>> BooksAsync(Guid companyId)
    {
        var rows = await (from i in db.CommercialInvoices.AsNoTracking()
                          join p in db.BusinessPartners.AsNoTracking() on i.PartnerId equals p.Id
                          where i.CompanyId == companyId && i.Kind == InvoiceKind.Purchase && (i.Status == InvoiceStatus.Posted || i.Status == InvoiceStatus.Draft)
                          select new { i.Id, i.Number, p.TaxCode, i.InvoiceSeries, i.InvoiceNo, i.Purpose, i.Status }).ToListAsync();
        return rows.Select(r => new Book(r.Id, r.Number, Norm(r.TaxCode), r.InvoiceSeries.Trim(), r.InvoiceNo.Trim(), r.Purpose, r.Status)).ToList();
    }
}
