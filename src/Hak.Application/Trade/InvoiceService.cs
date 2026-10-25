using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Security;
using Hak.Domain.Production;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record InvoiceLineInput(decimal UnitPrice, Guid? StockDocumentId = null, int? StockLineNo = null, decimal? StockQuantity = null,
    PricingBasis Basis = PricingBasis.Quantity, decimal? Quantity = null, Guid? AccountId = null, decimal? VatRate = null, string? Description = null,
    Guid? OrderId = null, int? OrderLineNo = null,
    /// <summary>Chiết khấu thương mại ghi trên hóa đơn: tỷ lệ % trên tiền hàng, HOẶC số tiền (không nhập cả hai). Để trống thì lấy tỷ lệ của dòng đơn hàng gắn với dòng này (nếu có).</summary>
    decimal? DiscountPercent = null, decimal? DiscountAmount = null);

public sealed record InvoiceInput(InvoiceKind Kind, Guid PartnerId, string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate, DateOnly PostingDate,
    string Description, IReadOnlyList<InvoiceLineInput> Lines, DateOnly? DueDate = null, string? Currency = null, decimal? ExchangeRate = null,
    decimal? PaymentDiscountPercent = null, int? PaymentDiscountDays = null,
    /// <summary>Lý do cố ý lập hóa đơn từ phiếu kho của đối tượng khác; chỉ có tác dụng với người có quyền invoice.partner_override và được ghi nhật ký.</summary>
    string? PartnerOverrideReason = null,
    /// <summary>Hóa đơn sẽ lấy ký hiệu và số từ nhà cung cấp hóa đơn điện tử khi phát hành (không nhập tay): lưu số tạm cho tới lúc phát hành.</summary>
    bool AwaitIssue = false,
    /// <summary>Hóa đơn thay thế: hóa đơn bán gốc đã ghi sổ bị thay. Khi hóa đơn này ghi sổ, hóa đơn gốc được đảo trong cùng giao dịch; số lượng nguồn của hóa đơn gốc không bị tính hai lần.</summary>
    Guid? ReplacesInvoiceId = null);

public sealed record PriceDifferenceWarning(int LineNo, string ReceiptNumber, decimal ReceiptValue, decimal InvoiceValue, decimal Difference,
    decimal ReceiptQuantity, decimal QuantityInStock, decimal InventoryPart, decimal CogsPart, string Method);

public sealed record UninvoicedLine(Guid StockDocumentId, string StockDocumentNumber, DateOnly PostingDate, int LineNo, Guid ItemId, string ItemCode,
    decimal Quantity, decimal InvoicedQuantity, decimal? Bdmt, Guid? PartnerId);

/// <summary>
/// Purchase and sales invoices (master prompt §12–13). Account roles used (company configuration, never hard-coded):
/// AP_TRADE, AR_TRADE, GRNI (goods received not invoiced), VAT_INPUT, VAT_OUTPUT, SALES_REVENUE.
/// Purchase: stock receipts are carried on GRNI; the invoice moves GRNI to AP (+ input VAT). A Draft receipt is valued
/// from the invoice (BDMT × price) and posted with it. Sales: the issue already booked cost of goods; the invoice books
/// AR / revenue / output VAT. A stock line can never be invoiced beyond its quantity (no double revenue).
/// </summary>
public sealed partial class InvoiceService(HakDbContext db, AccountingEngine engine, StockService stock, AuditService audit, IClock clock)
{
    public const string Source = "HAK.INVOICE";
    /// <summary>Ký hiệu tạm của hóa đơn chờ phát hành điện tử; được thay bằng ký hiệu và số thật của nhà cung cấp khi phát hành.</summary>
    public const string AwaitingSeries = "CHO-PHAT-HANH";

    public async Task<CommercialInvoice> CreateAsync(SessionContext ctx, InvoiceInput input)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (input.Lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Hóa đơn chưa có dòng.");
        if (!input.AwaitIssue && (string.IsNullOrWhiteSpace(input.InvoiceNo) || string.IsNullOrWhiteSpace(input.InvoiceSeries)))
            throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập ký hiệu và số hóa đơn.");
        if (string.IsNullOrWhiteSpace(input.Description)) throw new DomainException(ErrorCodes.ValidationFailed, "Diễn giải bắt buộc.");
        if (input.AwaitIssue && input.Kind != InvoiceKind.Sales) throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ hóa đơn bán do công ty phát hành; hóa đơn mua nhận từ nhà cung cấp.");
        return await db.InTransactionAsync(async () =>
        {
            if (input.ReplacesInvoiceId is { } replaced)
            {
                var orig = await db.CommercialInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == replaced && i.CompanyId == ctx.CompanyId)
                           ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn bị thay thế không tồn tại.");
                if (orig.Kind != input.Kind || orig.Purpose != InvoicePurpose.Normal || orig.Status != InvoiceStatus.Posted)
                    throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ thay thế hóa đơn thường cùng chiều đã ghi sổ.");
                if (await db.CommercialInvoices.AnyAsync(i => i.ReplacesInvoiceId == replaced && i.Status == InvoiceStatus.Draft))
                    throw new DomainException(ErrorCodes.DuplicateSource, $"Hóa đơn {orig.Number} đã có hóa đơn thay thế đang chờ; hủy bản đó trước.");
            }
            if (!await db.BusinessPartners.AnyAsync(p => p.Id == input.PartnerId && p.CompanyId == ctx.CompanyId))
                throw new DomainException(ErrorCodes.PartnerWrongCompany, "Đối tượng không thuộc công ty.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            var (currency, rate, curDp, funcDp) = await CurrencyAsync(company, input.Currency, input.ExchangeRate);
            if (!input.AwaitIssue) await CheckDuplicateAsync(ctx.CompanyId, input.Kind, input.PartnerId, input.InvoiceSeries.Trim(), input.InvoiceNo.Trim(), null);

            var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == ctx.CompanyId && f.StartDate <= input.PostingDate && f.EndDate >= input.PostingDate)
                     ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {input.PostingDate.Vn()}.");
            // Chiết khấu thanh toán: lấy điều khoản nhập tay, nếu để trống thì điền sẵn theo đối tượng.
            var partnerTerms = await db.BusinessPartners.AsNoTracking().Where(p => p.Id == input.PartnerId).Select(p => new { p.PaymentDiscountPercent, p.PaymentDiscountDays }).FirstAsync();
            var discPct = input.PaymentDiscountPercent ?? (input.PaymentDiscountDays is null ? partnerTerms.PaymentDiscountPercent : null);
            var discDays = input.PaymentDiscountDays ?? (input.PaymentDiscountPercent is null ? partnerTerms.PaymentDiscountDays : null);
            if ((discPct is null) != (discDays is null)) throw new DomainException(ErrorCodes.ValidationFailed, "Chiết khấu thanh toán phải nhập đủ cả tỷ lệ % và số ngày.");
            if (discPct is { } dp0 && (dp0 <= 0 || dp0 > 100 || discDays is < 1 or > 365)) throw new DomainException(ErrorCodes.ValidationFailed, "Chiết khấu thanh toán: tỷ lệ lớn hơn 0 đến 100%, số ngày từ 1 đến 365.");
            var inv = new CommercialInvoice
            {
                PaymentDiscountPercent = discPct, PaymentDiscountDays = discDays, ReplacesInvoiceId = input.ReplacesInvoiceId,
                Id = Ids.New(), CompanyId = ctx.CompanyId, Kind = input.Kind,
                InvoiceSeries = input.AwaitIssue ? AwaitingSeries : input.InvoiceSeries.Trim(), InvoiceNo = input.AwaitIssue ? "CHO-" + Ids.New().ToString("N")[..12] : input.InvoiceNo.Trim(),
                InvoiceDate = input.InvoiceDate, PostingDate = input.PostingDate, DueDate = input.DueDate, PartnerId = input.PartnerId,
                Currency = currency, ExchangeRate = rate, Description = input.Description.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
            };
            inv.Number = await NextNumberAsync(ctx.CompanyId, fy, input.Kind == InvoiceKind.Purchase ? "HDM" : "HDB");
            var n = 0;
            var partnerOverrides = new List<string>();
            foreach (var l in input.Lines)
                inv.Lines.Add(await BuildLineAsync(ctx, inv, l, ++n, curDp, funcDp, input.PartnerOverrideReason, partnerOverrides));
            await CheckTotalsAgainstSourcesAsync(ctx.CompanyId, inv);
            if (partnerOverrides.Count > 0) inv.PartnerOverrideReason = input.PartnerOverrideReason!.Trim();
            db.CommercialInvoices.Add(inv);
            await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.create", "invoice", inv.Id.ToString(),
                new { inv.Number, inv.Kind, inv.InvoiceSeries, inv.InvoiceNo, total = inv.Lines.Sum(x => x.FunctionalAmount + x.VatAmount) });
            if (partnerOverrides.Count > 0)
                await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.partner_override", "invoice", inv.Id.ToString(), new { inv.Number, reason = input.PartnerOverrideReason!.Trim(), documents = partnerOverrides });
            await db.SaveChangesAsync();
            return inv;
        });
    }

    /// <summary>
    /// Posts the invoice (and, for purchases, any Draft receipt it values) in one transaction. Re-checks quantities
    /// and BDMT against the current measurements so a superseded certificate cannot be invoiced at a stale value.
    /// </summary>
    /// <summary>
    /// Price differences the posting would book (purchase invoices against receipts already posted at another value):
    /// the part still in stock adjusts inventory, the part already issued goes to cost of goods sold (role COGS).
    /// Shown to the user before posting; posting requires explicit acknowledgement when this list is not empty.
    /// </summary>
    public async Task<IReadOnlyList<PriceDifferenceWarning>> PriceDifferencesAsync(SessionContext ctx, Guid invoiceId)
    {
        ctx.Require(Permissions.VoucherView);
        var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                  ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
        var result = new List<PriceDifferenceWarning>();
        if (inv.Kind != InvoiceKind.Purchase || inv.Purpose != InvoicePurpose.Normal) return result;
        foreach (var l in inv.Lines.Where(x => x.StockDocumentId is not null).OrderBy(x => x.LineNo))
        {
            var receipt = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstAsync(d => d.Id == l.StockDocumentId);
            if (receipt.Status != StockDocumentStatus.Posted) continue;
            var rl = receipt.Lines.Single(x => x.LineNo == l.StockLineNo);
            var diff = l.FunctionalAmount - (rl.Amount ?? 0);
            if (diff == 0) continue;
            var split = await stock.PriceDifferenceSplitAsync(ctx.CompanyId, receipt.Id, rl.LineNo, diff);
            result.Add(new PriceDifferenceWarning(l.LineNo, receipt.Number, rl.Amount ?? 0, l.FunctionalAmount, diff, split.ReceiptQuantity, split.QuantityInStock,
                split.InStockPart, split.IssuedPart, split.Method.ToString()));
        }
        return result;
    }

    public async Task<CommercialInvoice> PostAsync(SessionContext ctx, Guid invoiceId, bool acknowledgePriceDifferences = false, long? expectedRowVersion = null)
    {
        ctx.Require(Permissions.VoucherPost);
        return await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (expectedRowVersion is { } rv && inv.RowVersion != rv)
                throw new DomainException(ErrorCodes.ConcurrencyConflict, $"Hóa đơn {inv.Number} đã được thay đổi (phiên bản {inv.RowVersion}, bạn đang xem phiên bản {rv}). Vui lòng tải lại.");
            if (inv.Status != InvoiceStatus.Draft)
                throw new DomainException(inv.Status == InvoiceStatus.Posted ? ErrorCodes.AlreadyPosted : ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number} ở trạng thái {inv.Status}.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && inv.CreatedBy == ctx.UserId)
                throw new DomainException(ErrorCodes.SodViolation, "Người ghi sổ hóa đơn không được trùng người lập.");
            if (inv.InvoiceSeries == AwaitingSeries)
                throw new DomainException(ErrorCodes.ValidationFailed, $"Hóa đơn {inv.Number} đang chờ phát hành điện tử: ký hiệu và số do nhà cung cấp cấp khi phát hành. Dùng Phát hành hóa đơn điện tử, không ghi sổ với số tạm.");
            await CheckDuplicateAsync(ctx.CompanyId, inv.Kind, inv.PartnerId, inv.InvoiceSeries, inv.InvoiceNo, inv.Id);
            if (inv.Purpose != InvoicePurpose.Normal) return await PostAdjustmentCoreAsync(ctx, inv);
            if (inv.ReplacesInvoiceId is { } replaced)
            {
                // Thay thế: đảo hóa đơn gốc ngay trước khi ghi sổ hóa đơn mới, cùng một giao dịch. Nếu bước nào lỗi (kỳ khóa, còn điều chỉnh…) thì cả hai đều không đổi.
                await ReverseAsync(ctx, replaced, inv.PostingDate, $"Thay thế bằng hóa đơn {inv.InvoiceSeries}/{inv.InvoiceNo}");
            }
            var warnings = await PriceDifferencesAsync(ctx, inv.Id);
            if (warnings.Count > 0 && !acknowledgePriceDifferences)
                throw new DomainException(ErrorCodes.PriceDifference,
                    $"Hóa đơn lệch giá với {warnings.Count} dòng phiếu nhập đã ghi sổ (tổng {warnings.Sum(w => w.Difference)}): " +
                    $"điều chỉnh tồn kho {warnings.Sum(w => w.InventoryPart)}, điều chỉnh giá vốn {warnings.Sum(w => w.CogsPart)}. Xác nhận để ghi sổ.",
                    warnings.Select(w => new ValidationError(ErrorCodes.PriceDifference,
                        $"Phiếu {w.ReceiptNumber}: nhập kho {w.ReceiptValue}, hóa đơn {w.InvoiceValue}, chênh {w.Difference} → tồn kho {w.InventoryPart} (còn {w.QuantityInStock}/{w.ReceiptQuantity}), giá vốn {w.CogsPart}", w.LineNo)).ToList());
            var itemAccounts = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).Where(i => i.InventoryAccountId != null).ToDictionaryAsync(i => i.Id, i => i.InventoryAccountId!.Value);

            var roles = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId).ToDictionaryAsync(m => m.RoleCode, m => m.AccountId);
            Guid Role(string code) => roles.TryGetValue(code, out var id) ? id
                : throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {code}.");

            await CheckTotalsAgainstSourcesAsync(ctx.CompanyId, inv);
            var gl = new List<VoucherLineInput>();
            foreach (var l in inv.Lines.OrderBy(x => x.LineNo))
            {
                await RecheckLineAsync(ctx, inv, l);
                if (inv.Kind == InvoiceKind.Purchase)
                {
                    var debit = l.StockDocumentId is null ? l.AccountId!.Value : Role("GRNI");
                    if (l.StockDocumentId is { } sd)
                    {
                        var receipt = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstAsync(d => d.Id == sd);
                        var rl = receipt.Lines.Single(x => x.LineNo == l.StockLineNo);
                        if (receipt.Status == StockDocumentStatus.Draft)
                        {
                            if (l.StockQuantity != rl.Quantity)
                                throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {l.LineNo}: hóa đơn định giá phiếu nhập nháp phải bao trọn số lượng dòng phiếu.");
                            await stock.SetDraftReceiptValueAsync(ctx.CompanyId, receipt.Id, rl.LineNo, l.FunctionalAmount, Role("GRNI"));
                            await stock.PostDocumentAsync(ctx, receipt.Id);
                        }
                        else
                        {
                            if (rl.CounterAccountId != Role("GRNI") || l.StockQuantity != rl.Quantity)
                                throw new DomainException(ErrorCodes.PriceDifference,
                                    $"Dòng {l.LineNo}: phiếu {receipt.Number} không ghi vào TK hàng về chưa có hóa đơn hoặc hóa đơn không bao trọn dòng phiếu; không đối trừ tự động được.");
                            var diff = l.FunctionalAmount - rl.Amount!.Value;
                            if (diff != 0)
                            {
                                // Policy confirmed by the business (2026-10-15): stock still on hand → inventory value; goods already issued → COGS.
                                var split = await stock.AdjustReceiptValueAsync(ctx, receipt.Id, rl.LineNo, diff, inv.PostingDate, inv.Id,
                                    $"Chênh lệch giá HĐ {inv.InvoiceSeries}/{inv.InvoiceNo}");
                                gl.Add(new VoucherLineInput(Role("GRNI"), Side.Debit, rl.Amount.Value, inv.PartnerId, Description: l.Description));
                                if (split.InStockPart != 0)
                                    gl.Add(new VoucherLineInput(itemAccounts[rl.ItemId], split.InStockPart > 0 ? Side.Debit : Side.Credit, Math.Abs(split.InStockPart),
                                        Description: $"Điều chỉnh giá trị tồn kho theo HĐ {inv.InvoiceNo}"));
                                if (split.IssuedPart != 0)
                                    gl.Add(new VoucherLineInput(Role("COGS"), split.IssuedPart > 0 ? Side.Debit : Side.Credit, Math.Abs(split.IssuedPart),
                                        Description: $"Điều chỉnh giá vốn hàng đã xuất theo HĐ {inv.InvoiceNo}"));
                                continue;
                            }
                        }
                    }
                    // GRNI is followed per supplier (company decision 16/10/2026: a 331 detail account), as the receipt posted it.
                    gl.Add(new VoucherLineInput(debit, Side.Debit, l.FunctionalAmount, l.StockDocumentId is null ? null : inv.PartnerId, Description: l.Description));
                }
                else
                {
                    var revenue = l.AccountId ?? Role("SALES_REVENUE");
                    gl.Add(new VoucherLineInput(revenue, Side.Credit, l.FunctionalAmount, Description: l.Description));
                }
            }
            var vat = inv.Lines.Sum(x => x.VatAmount);
            var total = inv.Lines.Sum(x => x.FunctionalAmount) + vat;
            if (inv.Kind == InvoiceKind.Purchase)
            {
                if (vat > 0) gl.Add(new VoucherLineInput(Role("VAT_INPUT"), Side.Debit, vat, Description: "Thuế GTGT đầu vào"));
                gl.AddRange(await PartnerLinesAsync(ctx.CompanyId, inv, Role("AP_TRADE"), Side.Credit, $"HĐ {inv.InvoiceSeries}/{inv.InvoiceNo}", inv.DueDate, null));
            }
            else
            {
                gl.InsertRange(0, await PartnerLinesAsync(ctx.CompanyId, inv, Role("AR_TRADE"), Side.Debit, $"HĐ {inv.InvoiceSeries}/{inv.InvoiceNo}", inv.DueDate, null));
                if (vat > 0) gl.Add(new VoucherLineInput(Role("VAT_OUTPUT"), Side.Credit, vat, Description: "Thuế GTGT đầu ra"));
            }
            gl.RemoveAll(x => x.Amount == 0);

            var v = await engine.PostSystemVoucherAsync(ctx, new VoucherInput(inv.Kind == InvoiceKind.Purchase ? VoucherTypes.PurchaseInvoice : VoucherTypes.SalesInvoice,
                inv.InvoiceDate, inv.PostingDate, $"{inv.Description} (HĐ {inv.InvoiceSeries}/{inv.InvoiceNo})", gl, SourceSystem: Source, SourceId: inv.Id.ToString()));
            inv.VoucherId = v.Id;
            inv.Status = InvoiceStatus.Posted;
            inv.PostedBy = ctx.UserId;
            inv.PostedAt = clock.UtcNow;
            inv.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.post", "invoice", inv.Id.ToString(),
                new { inv.Number, voucher = v.Number, total, priceDifferencesAcknowledged = warnings }, inv.RowVersion);
            await db.SaveChangesAsync();
            return inv;
        });
    }

    /// <summary>Reverses the invoice voucher (settlements released by the engine). Receipts stay on GRNI awaiting a new invoice.</summary>
    public async Task<CommercialInvoice> ReverseAsync(SessionContext ctx, Guid invoiceId, DateOnly reversalDate, string reason, long? expectedRowVersion = null)
    {
        ctx.Require(Permissions.VoucherReverse);
        return await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (expectedRowVersion is { } rv && inv.RowVersion != rv)
                throw new DomainException(ErrorCodes.ConcurrencyConflict, $"Hóa đơn {inv.Number} đã được thay đổi (phiên bản {inv.RowVersion}, bạn đang xem phiên bản {rv}). Vui lòng tải lại.");
            if (inv.Status != InvoiceStatus.Posted) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number} chưa ghi sổ hoặc đã đảo.");
            // Hóa đơn gốc còn điều chỉnh (trả hàng / giảm giá / điều chỉnh tăng) đã ghi sổ: phải đảo chứng từ phụ thuộc trước, nếu không điều chỉnh mồ côi
            // (giảm công nợ / giá trị kho trên một hóa đơn không còn). Kiểm trước mọi thao tác ghi nên thất bại không để lại gì dở dang.
            var dependents = await db.CommercialInvoices.AsNoTracking().Where(d => d.CompanyId == ctx.CompanyId && d.AdjustsInvoiceId == inv.Id && d.Status == InvoiceStatus.Posted)
                .OrderBy(d => d.Number).Select(d => d.Number).ToListAsync();
            if (dependents.Count > 0)
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number} còn hóa đơn điều chỉnh đã ghi sổ ({string.Join(", ", dependents)}). Đảo các hóa đơn điều chỉnh trước.");
            // Chốt của chị 24/10/2026: hóa đơn gắn phiếu xuất NVL hoặc phiếu nhập thành phẩm của lệnh sản xuất đã hoàn thành (Completed/Closed) thì CHẶN đảo —
            // giá thành của lệnh đã chốt trên giá kho hiện tại, đảo (tính lại đơn giá kho) sẽ làm giá thành lệch. Muốn đảo phải mở lại lệnh sản xuất theo quy trình riêng.
            var stockDocIds = await db.CommercialInvoiceLines.AsNoTracking().Where(l => l.InvoiceId == inv.Id && l.StockDocumentId != null).Select(l => l.StockDocumentId!.Value).Distinct().ToListAsync();
            if (stockDocIds.Count > 0)
            {
                var done = new[] { ProductionOrderStatus.Completed, ProductionOrderStatus.Closed };
                var finished = await db.ProductionOrders.AsNoTracking()
                    .Where(o => o.CompanyId == ctx.CompanyId && done.Contains(o.Status)
                                && (db.ProductionOrderIssues.Any(i => i.OrderId == o.Id && stockDocIds.Contains(i.StockDocumentId)) || (o.ReceiptDocumentId != null && stockDocIds.Contains(o.ReceiptDocumentId.Value))))
                    .OrderBy(o => o.Number).Select(o => o.Number).ToListAsync();
                if (finished.Count > 0)
                    throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number} gắn phiếu kho của lệnh sản xuất đã hoàn thành ({string.Join(", ", finished)}); không đảo được khi giá thành đã chốt.");
            }
            // Hóa đơn MUA: đảo tính lại đơn giá kho (phần đã xuất chuyển sang giá vốn). Nếu vật tư đó đã xuất cho lệnh sản xuất ĐÃ HOÀN THÀNH từ ngày nhập trở đi thì giá thành đã chốt
            // sẽ lệch → chặn (quy tắc thận trọng: không truy vết theo lô vì kho tính giá bình quân / FIFO theo kho; có thể chặn dư — muốn đảo phải mở lại lệnh sản xuất).
            if (inv.Kind == InvoiceKind.Purchase && stockDocIds.Count > 0)
            {
                var receipts = await db.StockDocuments.AsNoTracking().Where(d => stockDocIds.Contains(d.Id)).Select(d => new { d.PostingDate }).ToListAsync();
                var firstReceipt = receipts.Min(r => r.PostingDate);
                var itemIds = await db.StockDocumentLines.AsNoTracking().Where(l => stockDocIds.Contains(l.DocumentId)).Select(l => l.ItemId).Distinct().ToListAsync();
                var finishedStatuses = new[] { ProductionOrderStatus.Completed, ProductionOrderStatus.Closed };
                var consumed = await (from i in db.ProductionOrderIssues.AsNoTracking()
                                      join o in db.ProductionOrders.AsNoTracking() on i.OrderId equals o.Id
                                      join d in db.StockDocuments.AsNoTracking() on i.StockDocumentId equals d.Id
                                      join l in db.StockDocumentLines.AsNoTracking() on d.Id equals l.DocumentId
                                      where o.CompanyId == ctx.CompanyId && finishedStatuses.Contains(o.Status) && d.Status == StockDocumentStatus.Posted
                                            && d.PostingDate >= firstReceipt && itemIds.Contains(l.ItemId)
                                      select o.Number).Distinct().OrderBy(n => n).ToListAsync();
                if (consumed.Count > 0)
                    throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number}: vật tư đã xuất cho lệnh sản xuất đã hoàn thành ({string.Join(", ", consumed)}); đảo hóa đơn mua sẽ làm lệch giá thành đã chốt.");
            }
            // Tính lại đơn giá khi đảo: phần giá trị hóa đơn đã nằm trong hàng xuất được chuyển từ tồn kho sang giá vốn TRƯỚC khi ghi bút toán đảo,
            // để tài khoản tồn kho (có thể chặn số dư âm) không âm giữa chừng. Sổ kho: chỉ gỡ phần còn thuộc hàng tồn (adjustments bất biến, không xóa).
            var shifts = await stock.ReverseValueAdjustmentsAsync(ctx, inv.Id, reversalDate, $"Đảo hóa đơn {inv.InvoiceSeries}/{inv.InvoiceNo}", reprice: true);
            if (shifts.Count > 0) await PostRepriceShiftAsync(ctx, inv, shifts, reversalDate);
            await engine.ReverseCoreAsync(ctx, inv.VoucherId!.Value, reversalDate, $"Đảo hóa đơn {inv.InvoiceSeries}/{inv.InvoiceNo}: {reason}", allowModuleOwned: true);
            inv.Status = InvoiceStatus.Reversed;
            inv.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.reverse", "invoice", inv.Id.ToString(), new { inv.Number, reason }, inv.RowVersion);
            await db.SaveChangesAsync();
            return inv;
        });
    }

    /// <summary>
    /// The mirror reversal credits inventory for the whole inventory part booked at posting. If some of that stock has since been issued
    /// (its cost already went to COGS inside the issues), the over-credit on inventory is moved to COGS: Dr inventory / Cr COGS.
    /// </summary>
    private async Task PostRepriceShiftAsync(SessionContext ctx, CommercialInvoice inv, IReadOnlyList<StockService.RepriceShift> shifts, DateOnly date)
    {
        var cogs = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "COGS").Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync()
                   ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, "Chưa ánh xạ vai trò tài khoản COGS.");
        var itemIds = shifts.Select(x => x.ItemId).Distinct().ToList();
        var accounts = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.InventoryAccountId);
        var lines = new List<VoucherLineInput>();
        foreach (var g in shifts.GroupBy(x => x.ItemId))
        {
            var amount = g.Sum(x => x.Amount);
            if (amount == 0) continue;
            var inventory = accounts[g.Key] ?? throw new DomainException(ErrorCodes.ValidationFailed, "Vật tư chưa có tài khoản tồn kho.");
            var text = $"Tính lại giá khi đảo HĐ {inv.InvoiceNo}: phần hàng đã xuất chuyển từ tồn kho sang giá vốn";
            lines.Add(new VoucherLineInput(inventory, amount > 0 ? Side.Debit : Side.Credit, Math.Abs(amount), Description: text));
            lines.Add(new VoucherLineInput(cogs, amount > 0 ? Side.Credit : Side.Debit, Math.Abs(amount), Description: text));
        }
        if (lines.Count == 0) return;
        await engine.PostSystemVoucherAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Tính lại giá vốn khi đảo hóa đơn {inv.InvoiceSeries}/{inv.InvoiceNo}", lines,
            SourceSystem: "HAK.INVOICE", SourceId: inv.Id + ":reprice"));
    }

    public async Task<CommercialInvoice> CancelAsync(SessionContext ctx, Guid invoiceId, string reason, long? expectedRowVersion = null)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy.");
        return await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            RowVersionCheck.Require(expectedRowVersion, inv.RowVersion, $"Hóa đơn {inv.Number}");
            if (inv.Status != InvoiceStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ hủy được hóa đơn nháp.");
            inv.Status = InvoiceStatus.Cancelled;
            inv.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "invoice.cancel", "invoice", inv.Id.ToString(), new { inv.Number, reason }, inv.RowVersion);
            await db.SaveChangesAsync();
            return inv;
        });
    }

    /// <summary>
    /// Stock lines not yet (fully) invoiced: for purchases, receipts carried on GRNI (hàng về chưa có hóa đơn);
    /// for sales, deliveries not yet invoiced (đối chiếu xuất bán ↔ hóa đơn).
    /// </summary>
    public async Task<IReadOnlyList<UninvoicedLine>> UninvoicedAsync(SessionContext ctx, InvoiceKind kind, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var type = kind == InvoiceKind.Purchase ? StockDocumentType.Receipt : StockDocumentType.Issue;
        var lines = await (from d in db.StockDocuments
                           join l in db.StockDocumentLines on d.Id equals l.DocumentId
                           join i in db.Items on l.ItemId equals i.Id
                           where d.CompanyId == ctx.CompanyId && d.Type == type && d.Status == StockDocumentStatus.Posted && d.PostingDate <= asOf && d.ReversesDocumentId == null
                           select new { d.Id, d.Number, d.PostingDate, d.PartnerId, l.LineNo, ItemId = i.Id, ItemCode = i.Code, l.Quantity, l.CounterAccountId })
            .AsNoTracking().ToListAsync();
        if (kind == InvoiceKind.Purchase)
        {
            var grni = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "GRNI").Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync();
            lines = lines.Where(l => l.CounterAccountId == grni).ToList();
        }
        var result = new List<UninvoicedLine>();
        foreach (var l in lines)
        {
            var invoiced = await InvoicedQuantityAsync(ctx.CompanyId, l.Id, l.LineNo, null);
            if (invoiced >= l.Quantity) continue;
            var bdmt = await db.StockMeasurements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.DocumentId == l.Id && m.LineNo == l.LineNo && m.SupersededAt == null).Select(m => (decimal?)m.Bdmt).FirstOrDefaultAsync();
            result.Add(new UninvoicedLine(l.Id, l.Number, l.PostingDate, l.LineNo, l.ItemId, l.ItemCode, l.Quantity, invoiced, bdmt, l.PartnerId));
        }
        return result.OrderBy(r => r.PostingDate).ThenBy(r => r.StockDocumentNumber).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<CommercialInvoiceLine> BuildLineAsync(SessionContext ctx, CommercialInvoice inv, InvoiceLineInput l, int lineNo, int curDp, int funcDp,
        string? partnerOverrideReason = null, List<string>? partnerOverrides = null)
    {
        if (l.UnitPrice < 0 || !MoneyMath.HasAtMostDecimals(l.UnitPrice, MoneyMath.StorageScale))
            throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: đơn giá không hợp lệ (≥ 0, tối đa 4 chữ số thập phân).");
        if (l.VatRate is { } vr && (vr < 0 || vr > 100))
            throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: thuế suất không hợp lệ.");
        var line = new CommercialInvoiceLine
        {
            Id = Ids.New(), InvoiceId = inv.Id, CompanyId = ctx.CompanyId, LineNo = lineNo, Basis = l.Basis, UnitPrice = l.UnitPrice,
            VatRate = l.VatRate, AccountId = l.AccountId, Description = l.Description,
        };

        if (l.StockDocumentId is { } sd)
        {
            var doc = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == sd && d.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, $"Dòng {lineNo}: phiếu kho không tồn tại.");
            var expected = inv.Kind == InvoiceKind.Purchase ? StockDocumentType.Receipt : StockDocumentType.Issue;
            if (doc.Type != expected || doc.ReversesDocumentId is not null)
                throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: hóa đơn {(inv.Kind == InvoiceKind.Purchase ? "mua phải gắn phiếu nhập" : "bán phải gắn phiếu xuất")}.");
            var okStatus = inv.Kind == InvoiceKind.Purchase ? doc.Status is StockDocumentStatus.Draft or StockDocumentStatus.Posted : doc.Status == StockDocumentStatus.Posted;
            if (!okStatus) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Dòng {lineNo}: phiếu {doc.Number} ở trạng thái {doc.Status}.");
            var sl = doc.Lines.FirstOrDefault(x => x.LineNo == l.StockLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Dòng {lineNo}: phiếu {doc.Number} không có dòng {l.StockLineNo}.");
            line.StockDocumentId = doc.Id;
            line.StockLineNo = sl.LineNo;
            line.ItemId = sl.ItemId;
            line.OrderId = sl.OrderId;
            line.OrderLineNo = sl.OrderLineNo;
            if (sl.OrderId is { } so && (await db.TradeOrders.AsNoTracking().FirstAsync(o => o.Id == so)).PartnerId != inv.PartnerId)
                throw new DomainException(ErrorCodes.OrderMismatch, $"Dòng {lineNo}: đối tượng hóa đơn khác đối tượng của đơn hàng gắn với phiếu {doc.Number}.");
            // Phiếu kho (kể cả phiếu không có đơn) phải cùng đối tượng với hóa đơn; phiếu không ghi đối tượng thì không có gì để so.
            if (doc.PartnerId is { } docPartner && docPartner != inv.PartnerId)
            {
                // Ngoại lệ: người có quyền invoice.partner_override ghi lý do thì được lập; mọi trường hợp khác bị chặn. Ngoại lệ được ghi nhật ký ở CreateAsync.
                if (string.IsNullOrWhiteSpace(partnerOverrideReason) || partnerOverrides is null)
                    throw new DomainException(ErrorCodes.PartnerMismatch, $"Dòng {lineNo}: phiếu {doc.Number} thuộc đối tượng khác với đối tượng của hóa đơn.");
                ctx.Require(Permissions.InvoicePartnerOverride);
                partnerOverrides.Add(doc.Number);
            }
            line.StockQuantity = l.StockQuantity ?? sl.Quantity - await InvoicedQuantityAsync(ctx.CompanyId, doc.Id, sl.LineNo, null, inv.ReplacesInvoiceId);
            if (line.StockQuantity <= 0) throw new DomainException(ErrorCodes.OverInvoiced, $"Dòng {lineNo}: phiếu {doc.Number} dòng {sl.LineNo} đã lập hóa đơn đủ.");
            await CheckRemainingAsync(ctx.CompanyId, doc.Id, doc.Number, sl.LineNo, sl.Quantity, line.StockQuantity.Value, null, inv.ReplacesInvoiceId);

            if (l.Basis == PricingBasis.Bdmt)
            {
                var item = await db.Items.AsNoTracking().FirstAsync(i => i.Id == sl.ItemId);
                if (!item.TracksDryness) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: vật tư {item.Code} không theo dõi BDMT.");
                if (line.StockQuantity != sl.Quantity)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: tính giá theo BDMT phải lập hóa đơn cho trọn dòng phiếu (BDMT đo cho cả dòng).");
                line.BasisQuantity = await ActiveBdmtAsync(ctx.CompanyId, doc.Id, doc.Number, sl.LineNo);
            }
            else line.BasisQuantity = line.StockQuantity.Value;
        }
        else
        {
            if (l.Basis == PricingBasis.Bdmt) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: giá theo BDMT phải gắn phiếu kho có kết quả đo.");
            // Dòng dịch vụ gắn đơn hàng: vật tư của dòng đơn là dịch vụ → tài khoản mặc định theo dịch vụ, số lượng không vượt phần đã nghiệm thu xác nhận.
            Item? service = null;
            TradeOrder? svcOrder = null;
            if (l.OrderId is { } sid && l.OrderLineNo is { } sln)
            {
                svcOrder = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == sid && o.CompanyId == ctx.CompanyId);
                var sol = svcOrder?.Lines.FirstOrDefault(x => x.LineNo == sln);
                if (sol is not null) service = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == sol.ItemId && i.IsService);
            }
            var accountId = l.AccountId ?? (service is null ? null : inv.Kind == InvoiceKind.Sales ? service.RevenueAccountId : service.ExpenseAccountId);
            if (accountId is null || l.Quantity is not { } q || q <= 0)
                throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: dòng không gắn phiếu kho phải có tài khoản hạch toán và số lượng > 0.");
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.CompanyId == ctx.CompanyId && a.IsPostable && a.IsActive))
                throw new DomainException(ErrorCodes.AccountNotFound, $"Dòng {lineNo}: tài khoản không hợp lệ.");
            line.AccountId = accountId;
            line.BasisQuantity = q;
            if (service is not null)
            {
                line.ItemId = service.Id;
                await ServiceAcceptanceService.CheckInvoiceAgainstAcceptanceAsync(db, svcOrder!.Id, l.OrderLineNo!.Value, svcOrder.Number, q, inv.ReplacesInvoiceId);
            }
            if (l.OrderId is { } oid)
            {
                var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == oid && o.CompanyId == ctx.CompanyId)
                            ?? throw new DomainException(ErrorCodes.NotFound, $"Dòng {lineNo}: đơn hàng không tồn tại.");
                if (order.Kind != inv.Kind || order.PartnerId != inv.PartnerId || order.Status != OrderStatus.Confirmed || order.Lines.All(x => x.LineNo != l.OrderLineNo))
                    throw new DomainException(ErrorCodes.OrderMismatch, $"Dòng {lineNo}: đơn {order.Number} không khớp loại/đối tượng/trạng thái/dòng.");
                line.OrderId = oid;
                line.OrderLineNo = l.OrderLineNo;
            }
        }
        // Chiết khấu thương mại ghi trên hóa đơn: nhập tay (% hoặc số tiền) hoặc lấy tỷ lệ đã thỏa thuận trên dòng đơn hàng.
        if (l.DiscountPercent is not null && l.DiscountAmount is not null)
            throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: chiết khấu nhập theo % hoặc theo số tiền, không nhập cả hai.");
        var pct = l.DiscountPercent;
        if (pct is null && l.DiscountAmount is null && line.OrderId is { } discOrder && line.OrderLineNo is { } discLine)
            pct = await db.TradeOrderLines.AsNoTracking().Where(x => x.OrderId == discOrder && x.LineNo == discLine).Select(x => x.DiscountPercent).FirstOrDefaultAsync();
        if (pct is { } pp && (pp < 0 || pp > 100 || !MoneyMath.HasAtMostDecimals(pp, 4)))
            throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: tỷ lệ chiết khấu từ 0 đến 100%, tối đa 4 chữ số thập phân.");
        line.DiscountPercent = pct is > 0 ? pct : null;
        if (l.DiscountAmount is { } da)
        {
            var gross = MoneyMath.Round(line.BasisQuantity * line.UnitPrice, curDp);
            if (da < 0 || !MoneyMath.HasAtMostDecimals(da, curDp)) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: số tiền chiết khấu không hợp lệ (≥ 0, tối đa {curDp} chữ số thập phân).");
            if (da > gross) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {lineNo}: chiết khấu {da} vượt tiền hàng {gross}.");
            line.DiscountAmount = da;
        }
        Price(line, inv.ExchangeRate, curDp, funcDp);
        return line;
    }

    /// <summary>
    /// Tiền hàng = BasisQuantity × UnitPrice, trừ chiết khấu thương mại ghi trên hóa đơn (% thì tính trên tiền hàng, làm tròn theo tiền hóa đơn).
    /// <see cref="CommercialInvoiceLine.Amount"/> là số SAU chiết khấu: doanh thu, thuế GTGT, công nợ và giá nhập kho đều tính trên số này.
    /// </summary>
    private static void Price(CommercialInvoiceLine line, decimal? rate, int curDp, int funcDp)
    {
        var gross = MoneyMath.Round(line.BasisQuantity * line.UnitPrice, curDp);
        var discount = line.DiscountPercent is { } p ? MoneyMath.Round(gross * p / 100m, curDp) : line.DiscountAmount;
        line.DiscountAmount = discount;
        line.Amount = gross - discount;
        line.FunctionalAmount = rate is { } r ? MoneyMath.Convert(line.Amount, r, funcDp) : line.Amount;
        line.FunctionalDiscountAmount = discount == 0 ? 0 : rate is { } r2 ? MoneyMath.Convert(discount, r2, funcDp) : discount;
        line.VatAmount = line.VatRate is { } v ? MoneyMath.Round(line.FunctionalAmount * v / 100m, funcDp) : 0;
    }

    /// <summary>
    /// Tổng theo nguồn: nhiều dòng của MỘT hóa đơn cùng chỉ vào một dòng đơn dịch vụ hoặc một dòng phiếu kho phải được cộng lại rồi so với phần còn lại.
    /// Kiểm tra từng dòng riêng (BuildLineAsync lúc lập, RecheckLineAsync lúc ghi sổ — cả hai bỏ chính hóa đơn này ra) bỏ sót trường hợp 6 + 6 &gt; 10.
    /// Chạy trong cùng giao dịch ghi (một người ghi tại một thời điểm) nên không bị hai người dùng lách nhau.
    /// </summary>
    private async Task CheckTotalsAgainstSourcesAsync(Guid companyId, CommercialInvoice inv)
    {
        foreach (var g in inv.Lines.Where(x => x.StockDocumentId is null && x.ItemId is not null && x.OrderId is not null && x.OrderLineNo is not null)
                     .GroupBy(x => (OrderId: x.OrderId!.Value, LineNo: x.OrderLineNo!.Value)).Where(g => g.Count() > 1))
        {
            var itemId = g.First().ItemId!.Value;
            if (!await db.Items.AnyAsync(i => i.Id == itemId && i.IsService)) continue;
            var number = await db.TradeOrders.AsNoTracking().Where(o => o.Id == g.Key.OrderId).Select(o => o.Number).FirstAsync();
            await ServiceAcceptanceService.CheckInvoiceAgainstAcceptanceAsync(db, g.Key.OrderId, g.Key.LineNo, number, g.Sum(x => x.BasisQuantity), inv.ReplacesInvoiceId ?? inv.Id);
        }
        foreach (var g in inv.Lines.Where(x => x.StockDocumentId is not null && x.StockLineNo is not null)
                     .GroupBy(x => (Doc: x.StockDocumentId!.Value, LineNo: x.StockLineNo!.Value)).Where(g => g.Count() > 1))
        {
            var doc = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstAsync(d => d.Id == g.Key.Doc);
            var sl = doc.Lines.Single(x => x.LineNo == g.Key.LineNo);
            await CheckRemainingAsync(companyId, doc.Id, doc.Number, sl.LineNo, sl.Quantity, g.Sum(x => x.StockQuantity ?? 0), inv.Id, inv.ReplacesInvoiceId);
        }
    }

    /// <summary>At posting: quantities still available and the BDMT basis still equal to the active measurement.</summary>
    private async Task RecheckLineAsync(SessionContext ctx, CommercialInvoice inv, CommercialInvoiceLine l)
    {
        if (l.StockDocumentId is null && l.ItemId is { } svcItem && l.OrderId is { } svcOrder && l.OrderLineNo is { } svcLine && await db.Items.AnyAsync(i => i.Id == svcItem && i.IsService))
        {
            var number = await db.TradeOrders.AsNoTracking().Where(o => o.Id == svcOrder).Select(o => o.Number).FirstAsync();
            await ServiceAcceptanceService.CheckInvoiceAgainstAcceptanceAsync(db, svcOrder, svcLine, number, l.BasisQuantity, inv.Id);
            return;
        }
        if (l.StockDocumentId is not { } sd) return;
        var doc = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstAsync(d => d.Id == sd);
        if (inv.Kind == InvoiceKind.Sales && doc.Status != StockDocumentStatus.Posted || doc.Status is StockDocumentStatus.Reversed or StockDocumentStatus.Cancelled)
            throw new DomainException(ErrorCodes.InvalidStateTransition, $"Dòng {l.LineNo}: phiếu {doc.Number} ở trạng thái {doc.Status}.");
        var sl = doc.Lines.Single(x => x.LineNo == l.StockLineNo);
        // Đối tượng của phiếu có thể bị sửa (phiếu nhập nháp) sau khi lập hóa đơn: lệch đối tượng chỉ qua được khi hóa đơn đã được lập với ngoại lệ có lý do.
        if (doc.PartnerId is { } docPartner && docPartner != inv.PartnerId && string.IsNullOrWhiteSpace(inv.PartnerOverrideReason))
            throw new DomainException(ErrorCodes.PartnerMismatch, $"Dòng {l.LineNo}: phiếu {doc.Number} thuộc đối tượng khác với đối tượng của hóa đơn.");
        await CheckRemainingAsync(ctx.CompanyId, doc.Id, doc.Number, sl.LineNo, sl.Quantity, l.StockQuantity!.Value, inv.Id);
        if (l.Basis == PricingBasis.Bdmt && await ActiveBdmtAsync(ctx.CompanyId, doc.Id, doc.Number, sl.LineNo) != l.BasisQuantity)
            throw new DomainException(ErrorCodes.MeasurementPending,
                $"Dòng {l.LineNo}: BDMT của phiếu {doc.Number} đã thay đổi so với lúc lập hóa đơn (kết quả đo/chứng thư mới); hủy hóa đơn nháp và lập lại.");
        _ = ctx;
    }

    private async Task CheckRemainingAsync(Guid companyId, Guid docId, string docNumber, int lineNo, decimal lineQty, decimal wanted, Guid? excludeInvoice, Guid? alsoExclude = null)
    {
        var used = await InvoicedQuantityAsync(companyId, docId, lineNo, excludeInvoice, alsoExclude);
        if (used + wanted > lineQty)
            throw new DomainException(ErrorCodes.OverInvoiced, $"Phiếu {docNumber} dòng {lineNo}: đã lập hóa đơn {used}, thêm {wanted} vượt số lượng {lineQty}.");
    }

    /// <summary>Quantity of a stock line already on Draft or Posted invoices (cancelled/reversed invoices release it).</summary>
    private async Task<decimal> InvoicedQuantityAsync(Guid companyId, Guid docId, int lineNo, Guid? excludeInvoice, Guid? alsoExclude = null)
    {
        var rows = await (from l in db.CommercialInvoiceLines
                          join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                          where l.CompanyId == companyId && l.StockDocumentId == docId && l.StockLineNo == lineNo
                                && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted) && i.Id != excludeInvoice
                                && i.Id != alsoExclude                                                     // hóa đơn đang bị thay thế không tính hai lần cùng hóa đơn thay thế
                                && !db.CommercialInvoices.Any(r => r.ReplacesInvoiceId == i.Id && r.Status == InvoiceStatus.Draft && r.Id != excludeInvoice)
                          select l.StockQuantity).ToListAsync();
        var pending = db.CommercialInvoiceLines.Local.Where(l => l.StockDocumentId == docId && l.StockLineNo == lineNo && db.Entry(l).State == EntityState.Added && l.InvoiceId != excludeInvoice)
            .Sum(l => l.StockQuantity ?? 0);
        return rows.Sum(x => x ?? 0) + pending;
    }

    private async Task<decimal> ActiveBdmtAsync(Guid companyId, Guid docId, string docNumber, int lineNo)
        => await db.StockMeasurements.AsNoTracking().Where(m => m.CompanyId == companyId && m.DocumentId == docId && m.LineNo == lineNo && m.SupersededAt == null)
               .Select(m => (decimal?)m.Bdmt).FirstOrDefaultAsync()
           ?? throw new DomainException(ErrorCodes.MeasurementPending, $"Phiếu {docNumber} dòng {lineNo} chưa có kết quả đo độ khô / chứng thư; chưa tính được BDMT để lập hóa đơn.");

    private async Task CheckDuplicateAsync(Guid companyId, InvoiceKind kind, Guid partnerId, string series, string no, Guid? self)
    {
        var q = db.CommercialInvoices.Where(i => i.CompanyId == companyId && i.Kind == kind && i.InvoiceSeries == series && i.InvoiceNo == no
                                                 && i.Id != self && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Reversed);
        if (kind == InvoiceKind.Purchase) q = q.Where(i => i.PartnerId == partnerId); // supplier invoice identity includes the seller
        if (await q.AnyAsync())
            throw new DomainException(ErrorCodes.DuplicateInvoice, $"Hóa đơn {series}/{no} đã được ghi nhận.");
    }

    private async Task<(string? Currency, decimal? Rate, int CurrencyDp, int FunctionalDp)> CurrencyAsync(Company company, string? currency, decimal? rate)
    {
        var funcDp = await db.Currencies.Where(c => c.Code == company.FunctionalCurrency).Select(c => c.DecimalPlaces).FirstAsync();
        if (currency is null || currency == company.FunctionalCurrency) return (null, null, funcDp, funcDp);
        var dp = await db.Currencies.Where(c => c.Code == currency).Select(c => (int?)c.DecimalPlaces).FirstOrDefaultAsync()
                 ?? throw new DomainException(ErrorCodes.ValidationFailed, $"Đồng tiền {currency} chưa khai báo.");
        if (rate is not { } r || r <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn ngoại tệ phải có tỷ giá > 0.");
        if (!MoneyMath.HasAtMostDecimals(r, MoneyMath.RateScale)) throw new DomainException(ErrorCodes.AmountPrecision, "Tỷ giá tối đa 6 chữ số thập phân.");
        return (currency, r, dp, funcDp);
    }

    private Task<string> NextNumberAsync(Guid companyId, FiscalYear fy, string prefix) => Numbering.NextAsync(db, companyId, fy, "INV-" + prefix);
}
