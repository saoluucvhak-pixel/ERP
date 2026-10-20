using Hak.Domain.Catalog;
using Hak.Application.Accounting;
using Hak.Domain.Vouchers;
using Hak.Application.Inventory;
using Hak.Application.Trade;
using Hak.Application.Cycles;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public class TradeTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;
    private static DateOnly D(int m, int d) => new(2026, m, d);

    private sealed class Fx : IDisposable
    {
        public required TestHost H;
        public required Scope S;
        public required InvoiceService Inv;
        public Guid Wh, Dam, Add;
        public void Dispose() { S.Dispose(); H.Dispose(); }
        public Guid A(string code) => H.Acc[code];
        public Task<decimal> Bal(string acc, Guid? partner = null) => S.Ledger.AccountBalanceAsync(H.Auditor, A(acc), D(12, 31), partner);
    }

    /// <summary>Test-fixture accounts (neutral codes) mapped to the roles the invoice module needs.</summary>
    private static async Task<Fx> SetupAsync()
    {
        var h = await TestHost.CreateAsync();
        var s = h.NewScope();
        foreach (var (code, name) in new[] { ("INV", "Hàng tồn kho"), ("GRNI", "Hàng về chưa có hóa đơn"), ("VATIN", "Thuế GTGT đầu vào"), ("VATOUT", "Thuế GTGT đầu ra"), ("SALESREV", "Doanh thu bán hàng"), ("COGS", "Giá vốn"), ("SALESRET", "Hàng bán bị trả lại"), ("SALESDISC", "Giảm giá hàng bán") })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name + " (thử)")).Id;
        // GRNI followed per supplier (company decision 16/10/2026): every GRNI line must carry the supplier.
        await s.Setup.UpdateAccountAttributesAsync(h.Admin, h.Acc["GRNI"], true, Hak.Domain.Catalog.OpenItemNature.None, Hak.Domain.Catalog.CashKind.None,
            Hak.Domain.Catalog.NegativeBalancePolicy.None);
        foreach (var (role, code) in new[] { ("AR_TRADE", "AR"), ("AP_TRADE", "AP"), ("GRNI", "GRNI"), ("VAT_INPUT", "VATIN"), ("VAT_OUTPUT", "VATOUT"), ("SALES_REVENUE", "SALESREV"), ("SALES_RETURNS", "SALESRET"), ("SALES_DISCOUNT", "SALESDISC"), ("COGS", "COGS") })
            await s.Rules.SetRoleMappingAsync(h.Admin, role, code);
        var fx = new Fx { H = h, S = s, Inv = new InvoiceService(s.Db, s.Engine, s.Stock, s.Audit, h.Clock) };
        fx.Wh = (await s.Stock.CreateWarehouseAsync(h.Admin, "BAI", "Bãi dăm")).Id;
        await s.Stock.SetCostingPolicyAsync(h.Admin, fx.Wh, CostingMethod.MovingAverage, D(1, 1), "QĐ test");
        fx.Dam = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "tấn", "INV")).Id;
        fx.Add = (await s.Stock.CreateItemAsync(h.Admin, "PHU", "Phụ gia", "kg", "INV")).Id;
        await s.Dryness.SetItemTracksDrynessAsync(h.Admin, fx.Dam, true);
        await s.Dryness.CreateMethodAsync(h.Admin, "DO-KHO", "BDMT = MT × độ khô%", 2, 3, D(1, 1), "Doanh nghiệp xác nhận 14/10/2026");
        return fx;
    }

    private static async Task<StockDocument> DraftReceiptAsync(Fx fx, decimal mt, DateOnly date, Guid? item = null)
        => await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, date, date, "Nhập dăm chờ hóa đơn",
            [new StockLineInput(item ?? fx.Dam, mt, 0, fx.A("GRNI"))], PartnerId: fx.H.Partner["NCC01"]));

    private static async Task<StockDocument> PostedReceiptAsync(Fx fx, decimal qty, decimal value, DateOnly date, Guid item)
    {
        var d = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, date, date, "Nhập",
            [new StockLineInput(item, qty, value, fx.A("GRNI"))], PartnerId: fx.H.Partner["NCC01"]));
        return await fx.S.Stock.PostDocumentAsync(fx.H.Approver, d.Id);
    }

    private static async Task<StockDocument> PostedIssueAsync(Fx fx, decimal qty, DateOnly date, Guid? item = null)
    {
        var d = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, date, date, "Xuất bán",
            [new StockLineInput(item ?? fx.Dam, qty, CounterAccountId: fx.A("COGS"))]));
        return await fx.S.Stock.PostDocumentAsync(fx.H.Approver, d.Id);
    }

    private static InvoiceInput Purchase(Fx fx, string no, params InvoiceLineInput[] lines)
        => new(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "1C26TAA", no, D(3, 3), D(3, 3), "Mua dăm gỗ", lines, DueDate: D(4, 2));

    /// <summary>Posted purchase invoice of 100 kg additive at 50,000/kg + VAT 10% (receipt valued 5,000,000 through GRNI).</summary>
    private static async Task<CommercialInvoice> PostedPurchaseOfAdditiveAsync(Fx fx, string no = "0000300")
    {
        var receipt = await DraftReceiptAsync(fx, 100, D(3, 2), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, no, new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        return await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
    }

    [Fact]
    public async Task PurchaseReturn_IssuedAtCost_CreditNoteClearsGrni_GapToCogs_AndSettlesAp()
    {
        using var fx = await SetupAsync();
        var invoice = await PostedPurchaseOfAdditiveAsync(fx);
        await PostedReceiptAsync(fx, 100, 7_000_000, D(3, 4), fx.Add);          // moving average now 60,000/kg

        var issue = await fx.Inv.CreatePurchaseReturnIssueAsync(fx.H.Preparer, invoice.Id, 1, 20, D(3, 10));
        Assert.Equal(StockDocumentStatus.Draft, issue.Status);
        var credit = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invoice.Id, InvoicePurpose.Return, "1C26TAA", "0000301", D(3, 10), D(3, 10),
            "Trả 20 kg phụ gia kém chất lượng", [new AdjustmentLineInput(1, issue.Id, 1)]));
        Assert.Equal((1_000_000m, 100_000m), (credit.Lines[0].FunctionalAmount, credit.Lines[0].VatAmount));
        await fx.Inv.PostAsync(fx.H.Approver, credit.Id);

        // Issue at cost 20 × 60,000 = 1,200,000; supplier credits 1,000,000 + VAT → 200,000 to COGS.
        Assert.Equal(-4_400_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));
        Assert.Equal(400_000m, await fx.Bal("VATIN"));
        Assert.Equal(200_000m, await fx.Bal("COGS"));
        Assert.Equal(10_800_000m, await fx.Bal("INV"));
        Assert.Equal(-7_000_000m, await fx.Bal("GRNI"));                         // only the un-invoiced second receipt remains
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        var open = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AP"), fx.H.Partner["NCC01"], D(12, 31)));
        Assert.Equal(4_400_000m, open.Open);                                     // the credit note settled the original payable

        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreatePurchaseReturnIssueAsync(fx.H.Preparer, invoice.Id, 1, 81, D(3, 11))));
        var issue2 = await fx.Inv.CreatePurchaseReturnIssueAsync(fx.H.Preparer, invoice.Id, 1, 5, D(3, 11));
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invoice.Id, InvoicePurpose.Return,
            "1C26TAA", "0000302", D(3, 11), D(3, 11), "Lần 2", [new AdjustmentLineInput(1, issue.Id, 1)]))));       // issue already on a credit note
        await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invoice.Id, InvoicePurpose.Return, "1C26TAA", "0000302", D(3, 11), D(3, 11),
            "Lần 2", [new AdjustmentLineInput(1, issue2.Id, 1)]));
    }

    [Fact]
    public async Task PurchaseDiscount_SplitsBetweenStockAndCogs_AndReversalKeepsStockLedgerInLine()
    {
        using var fx = await SetupAsync();
        var invoice = await PostedPurchaseOfAdditiveAsync(fx);
        await PostedIssueAsync(fx, 40, D(3, 5), fx.Add);                       // 40 kg used: COGS 2,000,000
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invoice.Id, InvoicePurpose.Discount,
            "1C26TAA", "0000310", D(3, 12), D(3, 12), "Giảm giá", [new AdjustmentLineInput(1, Amount: 5_000_001)]))));

        var discount = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invoice.Id, InvoicePurpose.Discount, "1C26TAA", "0000310", D(3, 12), D(3, 12),
            "Giảm giá theo biên bản chất lượng", [new AdjustmentLineInput(1, Amount: 1_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, discount.Id);
        // 60 kg still in stock → −600,000 inventory; 40 kg issued → −400,000 COGS; VAT −100,000; AP −1,100,000.
        Assert.Equal(2_400_000m, await fx.Bal("INV"));
        Assert.Equal(1_600_000m, await fx.Bal("COGS"));
        Assert.Equal(400_000m, await fx.Bal("VATIN"));
        Assert.Equal(-4_400_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);

        await fx.Inv.ReverseAsync(fx.H.Approver, discount.Id, D(3, 13), "Nhà cung cấp hủy biên bản");
        Assert.Equal(3_000_000m, await fx.Bal("INV"));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        var summary = Assert.Single(await fx.S.Stock.SummaryAsync(fx.H.Auditor, D(1, 1), D(12, 31)), r => r.ItemId == fx.Add);
        Assert.Equal((60m, 3_000_000m), (summary.ClosingQty, summary.ClosingValue));
    }

    [Fact]
    public async Task Purchase_PricedPerBdmt_ValuesTheDraftReceipt_AndBooksApWithVat()
    {
        using var fx = await SetupAsync();
        var receipt = await DraftReceiptAsync(fx, 100, D(3, 2));
        Assert.Equal(ErrorCodes.MeasurementPending, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer,
            Purchase(fx, "0000123", new InvoiceLineInput(2_000_000, receipt.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8)))));

        await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(receipt.Id, 1, 52.35m, EvidenceType.InternalReport, "PĐ-1", D(3, 2)));
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "0000123", new InvoiceLineInput(2_000_000, receipt.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8)));
        var line = Assert.Single(inv.Lines);
        Assert.Equal((52.35m, 104_700_000m, 8_376_000m), (line.BasisQuantity, line.FunctionalAmount, line.VatAmount)); // 52.35 BDMT × 2,000,000

        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => fx.Inv.PostAsync(fx.H.Preparer with { Permissions = new HashSet<string>(fx.H.Preparer.Permissions) }, inv.Id)));
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
        Assert.Equal(StockDocumentStatus.Posted, (await fx.S.Db.StockDocuments.AsNoTracking().SingleAsync(d => d.Id == receipt.Id)).Status);
        Assert.Equal(104_700_000m, await fx.Bal("INV"));       // stock carried at MT, valued from BDMT × price
        Assert.Equal(0m, await fx.Bal("GRNI"));
        Assert.Equal(8_376_000m, await fx.Bal("VATIN"));
        Assert.Equal(-113_076_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));
        var open = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AP"), null, D(12, 31)));
        Assert.Equal(D(4, 2), open.DueDate);
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        Assert.Empty(await fx.Inv.UninvoicedAsync(fx.H.Auditor, InvoiceKind.Purchase, D(12, 31)));
    }

    [Fact]
    public async Task Sales_ExportInUsdPerBdmt_FromShipCertificate_NoDoubleRevenue()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 150_000_000, D(3, 1), fx.Dam);
        var issue = await PostedIssueAsync(fx, 60, D(3, 10));
        Assert.Equal(90_000_000m, await fx.Bal("COGS")); // cost by MT (moving average)

        var sale = new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TXK", "0000045", D(3, 12), D(3, 12), "Xuất khẩu dăm gỗ",
            [new InvoiceLineInput(180, issue.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 0)], DueDate: D(4, 11), Currency: "USD", ExchangeRate: 25_000);
        Assert.Equal(ErrorCodes.MeasurementPending, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, sale)));
        var cert = await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(issue.Id, 1, 55.5m, EvidenceType.Certificate, "CT-0456", D(3, 11), MeasuredMt: 59.2m));

        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, sale);
        var line = Assert.Single(inv.Lines);
        Assert.Equal((32.856m, 5_914.08m, 147_852_000m), (line.BasisQuantity, line.Amount, line.FunctionalAmount)); // 32.856 × 180 USD × 25,000
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, sale with { InvoiceNo = "0000046" })));

        await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
        Assert.Equal(147_852_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(-147_852_000m, await fx.Bal("SALESREV"));
        // The receivable is a foreign-currency open item, so it can be paid and revalued in USD.
        var receivable = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(3, 31)));
        Assert.Equal(("USD", 5_914.08m, 5_914.08m, 147_852_000m), (receivable.ForeignCurrency, receivable.ForeignAmount, receivable.OpenForeign, receivable.Open));
        Assert.Empty(await fx.Inv.UninvoicedAsync(fx.H.Auditor, InvoiceKind.Sales, D(12, 31)));
        Assert.Equal(ErrorCodes.DuplicateInvoice, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, sale)));

        // Reversal releases the delivery for re-invoicing; the cost of goods sold stays.
        await fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 15), "Sai tỷ giá");
        Assert.Equal(0m, await fx.Bal("SALESREV"));
        Assert.Equal(90_000_000m, await fx.Bal("COGS"));
        Assert.Single(await fx.Inv.UninvoicedAsync(fx.H.Auditor, InvoiceKind.Sales, D(12, 31)));
        _ = cert;
    }

    [Fact]
    public async Task SupersededCertificate_AfterDraftInvoice_BlocksPostingAtStaleBdmt()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 150_000_000, D(3, 1), fx.Dam);
        var issue = await PostedIssueAsync(fx, 60, D(3, 10));
        var cert = await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(issue.Id, 1, 55.5m, EvidenceType.Certificate, "CT-1", D(3, 11)));
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "9", D(3, 12), D(3, 12), "Bán",
            [new InvoiceLineInput(4_000_000, issue.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8)]));
        await fx.S.Dryness.SupersedeAsync(fx.H.Preparer, cert.Id, new MeasurementInput(issue.Id, 1, 54.9m, EvidenceType.Certificate, "CT-1-SUA", D(3, 13)), "Chứng thư điều chỉnh");
        Assert.Equal(ErrorCodes.MeasurementPending, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, inv.Id)));
        await fx.Inv.CancelAsync(fx.H.Preparer, inv.Id, "Lập lại theo chứng thư điều chỉnh");
        var again = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "9", D(3, 13), D(3, 13), "Bán",
            [new InvoiceLineInput(4_000_000, issue.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8)]));
        Assert.Equal(32.94m, again.Lines[0].BasisQuantity); // 60 × 54.9%
    }

    [Fact]
    public async Task PostedReceipt_MustMatchInvoiceValue_OtherwisePriceDifference()
    {
        using var fx = await SetupAsync();
        var r = await PostedReceiptAsync(fx, 10, 1_000_000, D(3, 1), fx.Add);
        Assert.Single(await fx.Inv.UninvoicedAsync(fx.H.Auditor, InvoiceKind.Purchase, D(12, 31))); // goods received, invoice pending
        var bad = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "777", new InvoiceLineInput(105_000, r.Id, 1, VatRate: 10)));
        Assert.Equal(ErrorCodes.PriceDifference, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, bad.Id)));
        await fx.Inv.CancelAsync(fx.H.Preparer, bad.Id, "Sai giá");
        var ok = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "778", new InvoiceLineInput(100_000, r.Id, 1, VatRate: 10)));
        await fx.Inv.PostAsync(fx.H.Approver, ok.Id);
        Assert.Equal((0m, 100_000m, -1_100_000m), (await fx.Bal("GRNI"), await fx.Bal("VATIN"), await fx.Bal("AP")));
    }

    [Fact]
    public async Task ServiceLines_DuplicateSupplierInvoice_AndPartialSalesInvoicing()
    {
        using var fx = await SetupAsync();
        var svc = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "555", new InvoiceLineInput(250_000, Quantity: 4, AccountId: fx.A("EXP"), VatRate: 10, Description: "Vận chuyển")));
        await fx.Inv.PostAsync(fx.H.Approver, svc.Id);
        Assert.Equal((1_000_000m, 100_000m), (await fx.Bal("EXP"), await fx.Bal("VATIN")));
        Assert.Equal(ErrorCodes.DuplicateInvoice, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "555", new InvoiceLineInput(1, Quantity: 1, AccountId: fx.A("EXP"))))));
        await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "555", new InvoiceLineInput(1, Quantity: 1, AccountId: fx.A("EXP"))) with { PartnerId = fx.H.Partner["KH02"] }); // other seller, same number: allowed

        await PostedReceiptAsync(fx, 10, 1_000, D(3, 1), fx.Add);
        var issue = await PostedIssueAsync(fx, 10, D(3, 2), fx.Add);
        InvoiceInput SalesLine(decimal q) => new(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", Guid.NewGuid().ToString("N")[..8], D(3, 3), D(3, 3), "Bán phụ gia",
            [new InvoiceLineInput(500, issue.Id, 1, StockQuantity: q, VatRate: 10)]);
        await fx.Inv.CreateAsync(fx.H.Preparer, SalesLine(4));
        await fx.Inv.CreateAsync(fx.H.Preparer, SalesLine(6));
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, SalesLine(1))));
        // BDMT pricing is only for dryness-tracked items.
        await PostedReceiptAsync(fx, 5, 500, D(3, 4), fx.Add);
        var issue2 = await PostedIssueAsync(fx, 5, D(3, 5), fx.Add);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", "x", D(3, 5), D(3, 5), "x",
            [new InvoiceLineInput(1, issue2.Id, 1, Basis: PricingBasis.Bdmt)]))));
    }

    [Fact]
    public async Task InvoiceVoucher_CannotBeReversedOutsideTheInvoice_AndPostedInvoiceIsFrozen()
    {
        using var fx = await SetupAsync();
        var svc = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "1", new InvoiceLineInput(100, Quantity: 1, AccountId: fx.A("EXP"))));
        svc = await fx.Inv.PostAsync(fx.H.Approver, svc.Id);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.S.Engine.ReverseAsync(fx.H.Approver, svc.VoucherId!.Value, D(3, 5), "x")));
        var tracked = await fx.S.Db.CommercialInvoices.Include(i => i.Lines).SingleAsync(i => i.Id == svc.Id);
        tracked.Lines[0].UnitPrice = 1;
        Assert.Equal(ErrorCodes.LedgerImmutable, (await Assert.ThrowsAsync<DomainException>(() => fx.S.Db.SaveChangesAsync())).Code);
    }

    /// <summary>Posted BDMT sale used by the adjustment tests: 60 MT issued (cost 90,000,000), 33.3 BDMT × 4,000,000 + 8% VAT.</summary>
    private static async Task<(CommercialInvoice Invoice, StockDocument Issue)> PostedBdmtSaleAsync(Fx fx)
    {
        await PostedReceiptAsync(fx, 100, 150_000_000, D(3, 1), fx.Dam);
        var issue = await PostedIssueAsync(fx, 60, D(3, 10));
        await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(issue.Id, 1, 55.5m, EvidenceType.Certificate, "CT-9", D(3, 11)));
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "100", D(3, 12), D(3, 12), "Bán dăm",
            [new InvoiceLineInput(4_000_000, issue.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8)], DueDate: D(4, 30)));
        return (await fx.Inv.PostAsync(fx.H.Approver, inv.Id), issue);
    }

    [Fact]
    public async Task SalesReturn_RestocksAtOriginalCost_ReducesRevenueByReturnBdmt_AndSettlesTheInvoice()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);
        Assert.Equal(143_856_000m, await fx.Bal("AR")); // 33.3 BDMT × 4,000,000 = 133,200,000 + 10,656,000 VAT

        var ret = await fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 10, D(3, 15));
        Assert.Equal(15_000_000m, ret.Lines[0].Amount);   // 90,000,000 × 10 / 60: back at the cost that was booked
        var adj = new AdjustmentInput(inv.Id, InvoicePurpose.Return, "1C26T", "101", D(3, 16), D(3, 16), "Khách trả lại 10 MT",
            [new AdjustmentLineInput(1, ret.Id, 1)]);
        Assert.Equal(ErrorCodes.MeasurementPending, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, adj))); // measured at receipt, like any receipt
        await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(ret.Id, 1, 50m, EvidenceType.InternalReport, "PĐ-TL", D(3, 15)));
        var note = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, adj);
        Assert.Equal((5m, 20_000_000m, 1_600_000m), (note.Lines[0].BasisQuantity, note.Lines[0].FunctionalAmount, note.Lines[0].VatAmount));
        await fx.Inv.PostAsync(fx.H.Approver, note.Id);

        Assert.Equal(75_000_000m, await fx.Bal("COGS"));
        Assert.Equal(75_000_000m, await fx.Bal("INV"));
        Assert.Equal(20_000_000m, await fx.Bal("SALESRET"));
        Assert.Equal(-9_056_000m, await fx.Bal("VATOUT"));
        Assert.Equal(122_256_000m, await fx.Bal("AR"));
        var open = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), null, D(12, 31)));
        Assert.Equal(122_256_000m, open.Open); // the credit note settled part of the original invoice
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);

        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 51, D(3, 20))));
    }

    [Fact]
    public async Task Discount_And_Increase_AdjustRevenueAndReceivable_WithinLimits()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);
        var disc = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Discount, "1C26T", "102", D(3, 20), D(3, 20),
            "Giảm giá do độ khô thấp", [new AdjustmentLineInput(1, Amount: 5_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, disc.Id);
        Assert.Equal(5_000_000m, await fx.Bal("SALESDISC"));
        Assert.Equal(143_856_000m - 5_400_000m, await fx.Bal("AR"));
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Discount,
            "1C26T", "103", D(3, 21), D(3, 21), "x", [new AdjustmentLineInput(1, Amount: 128_200_001)]))));

        var inc = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Increase, "1C26T", "104", D(3, 22), D(3, 22),
            "Điều chỉnh tăng phí", [new AdjustmentLineInput(1, Amount: 1_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, inc.Id);
        Assert.Equal(143_856_000m - 5_400_000m + 1_080_000m, await fx.Bal("AR"));
        Assert.Equal(-134_200_000m, await fx.Bal("SALESREV"));
        Assert.True((await fx.S.Settlement.ReconcileAsync(fx.H.Auditor, D(12, 31))).IsReconciled);
    }

    [Fact]
    public async Task Adjustments_OnlyForPostedNormalSalesInvoices()
    {
        using var fx = await SetupAsync();
        var p = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "9", new InvoiceLineInput(100, Quantity: 1, AccountId: fx.A("EXP"))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(p.Id, InvoicePurpose.Discount,
            "X", "1", D(3, 3), D(3, 3), "x", [new AdjustmentLineInput(1, Amount: 1)]))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, p.Id, 1, 1, D(3, 3))));
    }

    private static OrderService Orders(Fx fx) => new(fx.S.Db, fx.S.Audit, fx.H.Clock);

    [Fact]
    public async Task SalesOrder_ControlsDeliveries_WithTolerance_AndTracksChainToPayment()
    {
        using var fx = await SetupAsync();
        var orders = Orders(fx);
        await PostedReceiptAsync(fx, 200, 300_000_000, D(3, 1), fx.Dam);
        var order = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Sales, fx.H.Partner["KH01"], D(3, 2), "HĐ bán dăm số 01/2026",
            [new OrderLineInput(fx.Dam, 100, 4_000_000, PricingBasis.Bdmt, 8)], ContractNo: "01/2026/HĐMB", OverDeliveryTolerancePercent: 5));

        StockDocumentInput Issue(decimal q) => new(StockDocumentType.Issue, fx.Wh, D(3, 10), D(3, 10), "Giao theo đơn",
            [new StockLineInput(fx.Dam, q, CounterAccountId: fx.A("COGS"), OrderId: order.Id, OrderLineNo: 1)], PartnerId: fx.H.Partner["KH01"]);
        Assert.Equal(ErrorCodes.OrderMismatch, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(60)))); // not confirmed yet
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => orders.ConfirmAsync(fx.H.Preparer with { Permissions = new HashSet<string>(fx.H.Preparer.Permissions) { "voucher.approve" } }, order.Id)));
        await orders.ConfirmAsync(fx.H.Approver, order.Id);

        var d1 = await fx.S.Stock.PostDocumentAsync(fx.H.Approver, (await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(60))).Id);
        var d2draft = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(40));
        Assert.Equal(ErrorCodes.OverDelivered, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(6)))); // 100 + 5% = 105 max
        await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(5));                                                        // exactly 105 allowed
        Assert.Equal(ErrorCodes.OrderMismatch, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(1) with { PartnerId = fx.H.Partner["KH02"] })));
        await fx.S.Stock.CancelDocumentAsync(fx.H.Preparer, d2draft.Id, "Khách lùi lịch");
        await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, Issue(35)); // cancelled draft no longer counts

        await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(d1.Id, 1, 55.5m, EvidenceType.Certificate, "CT-ORD", D(3, 11)));
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "300", D(3, 12), D(3, 12), "Bán theo đơn",
            [new InvoiceLineInput(4_000_000, d1.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8)]));
        Assert.Equal(order.Id, inv.Lines[0].OrderId);
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id);

        // Customer pays half by bank, settling the invoice.
        await Phase2Fixture.ConfigureAsync(fx.H, fx.S);
        var arLine = Phase2Fixture.LineOf(fx.S, inv.VoucherId!.Value, fx.A("AR"));
        await Phase2Fixture.PostDocAsync(fx.H, fx.S, Phase2Fixture.Receipt(fx.H, 71_928_000, D(3, 20), arLine, main: "BANK"));

        var f = await orders.FulfilmentAsync(fx.H.Auditor, order.Id);
        var line = Assert.Single(f.Lines);
        Assert.Equal((100m, 60m, 40m, 33.3m, 60m, 133_200_000m, 40m), (line.Ordered, line.StockPosted, line.StockDraft, line.Bdmt, line.Invoiced, line.InvoicedAmount, line.Outstanding));
        var iv = Assert.Single(f.Invoices);
        Assert.Equal((143_856_000m, 71_928_000m, 71_928_000m), (iv.Total, iv.Settled, iv.Open));

        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => orders.CancelAsync(fx.H.Preparer, order.Id, "x")));
        await orders.CloseAsync(fx.H.Preparer, order.Id, "Khách không lấy phần còn lại");
        var pending = (await fx.S.Db.StockDocuments.AsNoTracking().Where(d => d.Status == StockDocumentStatus.Draft).ToListAsync()).First();
        Assert.Equal(ErrorCodes.OrderMismatch, await CodeOf(() => fx.S.Stock.PostDocumentAsync(fx.H.Approver, pending.Id))); // closed order: drafts cannot post
    }

    [Fact]
    public async Task PurchaseOrder_LinksReceiptAndServiceLines_AndRejectsWrongItemOrKind()
    {
        using var fx = await SetupAsync();
        var orders = Orders(fx);
        var po = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], D(3, 1), "Mua dăm",
            [new OrderLineInput(fx.Dam, 50, 2_000_000, PricingBasis.Bdmt, 8)]));
        await orders.ConfirmAsync(fx.H.Approver, po.Id);
        Assert.Equal(ErrorCodes.OrderMismatch, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh,
            D(3, 2), D(3, 2), "x", [new StockLineInput(fx.Add, 1, 0, fx.A("GRNI"), OrderId: po.Id, OrderLineNo: 1)], PartnerId: fx.H.Partner["NCC01"]))));
        Assert.Equal(ErrorCodes.OrderMismatch, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh,
            D(3, 2), D(3, 2), "x", [new StockLineInput(fx.Dam, 1, CounterAccountId: fx.A("COGS"), OrderId: po.Id, OrderLineNo: 1)], PartnerId: fx.H.Partner["NCC01"]))));

        var receipt = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 2), D(3, 2), "Nhập theo đơn",
            [new StockLineInput(fx.Dam, 50, 0, fx.A("GRNI"), OrderId: po.Id, OrderLineNo: 1)], PartnerId: fx.H.Partner["NCC01"]));
        await fx.S.Dryness.RecordAsync(fx.H.Preparer, new MeasurementInput(receipt.Id, 1, 50m, EvidenceType.InternalReport, "PĐ", D(3, 2)));
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "800",
            new InvoiceLineInput(2_000_000, receipt.Id, 1, Basis: PricingBasis.Bdmt, VatRate: 8),
            new InvoiceLineInput(500_000, Quantity: 1, AccountId: fx.A("EXP"), VatRate: 10, Description: "Cước vận chuyển", OrderId: po.Id, OrderLineNo: 1)));
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
        var f = await orders.FulfilmentAsync(fx.H.Auditor, po.Id);
        Assert.Equal((50m, 25m, 50m, 0m), (f.Lines[0].StockPosted, f.Lines[0].Bdmt, f.Lines[0].Invoiced, f.Lines[0].Outstanding));
        Assert.Equal(50_000_000m + 500_000m, f.Lines[0].InvoicedAmount);
    }

    private static async Task<decimal> IssueValue(Fx fx, StockDocument doc)
    {
        var m = await fx.S.Db.StockMovements.AsNoTracking().SingleAsync(x => x.DocumentId == doc.Id);
        return -(await fx.S.Db.StockValuations.AsNoTracking().SingleAsync(v => v.MovementId == m.Id && v.VoidedAt == null)).Value;
    }

    [Fact]
    public async Task PriceDifference_MovingAverage_SplitsBetweenStockAndCogs_AfterAcknowledgedWarning()
    {
        using var fx = await SetupAsync();
        var r = await PostedReceiptAsync(fx, 10, 1_000_000, D(3, 1), fx.Add);          // provisional value on GRNI
        await PostedIssueAsync(fx, 4, D(3, 2), fx.Add);                                 // 400,000 to COGS
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "901", new InvoiceLineInput(110_000, r.Id, 1, VatRate: 10)));  // invoice 1,100,000

        var warn = Assert.Single(await fx.Inv.PriceDifferencesAsync(fx.H.Approver, inv.Id));
        Assert.Equal((100_000m, 6m, 60_000m, 40_000m), (warn.Difference, warn.QuantityInStock, warn.InventoryPart, warn.CogsPart));
        var ex = await Assert.ThrowsAsync<DomainException>(() => fx.Inv.PostAsync(fx.H.Approver, inv.Id));
        Assert.Equal(ErrorCodes.PriceDifference, ex.Code);  // warning: nothing posted until acknowledged
        Assert.Equal(0m, await fx.Bal("AP"));

        await fx.Inv.PostAsync(fx.H.Approver, inv.Id, acknowledgePriceDifferences: true);
        Assert.Equal((0m, 660_000m, 440_000m, -1_210_000m), (await fx.Bal("GRNI"), await fx.Bal("INV"), await fx.Bal("COGS"), await fx.Bal("AP")));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);

        var last = await PostedIssueAsync(fx, 6, D(3, 5), fx.Add);
        Assert.Equal(660_000m, await IssueValue(fx, last));    // the adjusted value flows out with the remaining stock
        Assert.Equal(0m, await fx.Bal("INV"));
        Assert.Equal(ErrorCodes.StockBackdated, await CodeOf(() => fx.S.Stock.ReverseDocumentAsync(fx.H.Approver, r.Id, D(3, 6), "x")));
    }

    [Fact]
    public async Task PriceDifference_Fifo_UsesTheReceiptsOwnRemainingLayer()
    {
        using var fx = await SetupAsync();
        var fifo = (await fx.S.Stock.CreateWarehouseAsync(fx.H.Admin, "K-FIFO", "Kho FIFO")).Id;
        await fx.S.Stock.SetCostingPolicyAsync(fx.H.Admin, fifo, CostingMethod.Fifo, D(1, 1), "QĐ test");
        async Task<StockDocument> Rcv(decimal v, DateOnly d) => await fx.S.Stock.PostDocumentAsync(fx.H.Approver, (await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer,
            new StockDocumentInput(StockDocumentType.Receipt, fifo, d, d, "Nhập", [new StockLineInput(fx.Add, 10, v, fx.A("GRNI"))], PartnerId: fx.H.Partner["NCC01"]))).Id);
        async Task<StockDocument> Iss(decimal q, DateOnly d) => await fx.S.Stock.PostDocumentAsync(fx.H.Approver, (await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer,
            new StockDocumentInput(StockDocumentType.Issue, fifo, d, d, "Xuất", [new StockLineInput(fx.Add, q, CounterAccountId: fx.A("COGS"))]))).Id);
        var a = await Rcv(1_000_000, D(3, 1));
        var b = await Rcv(1_200_000, D(3, 2));
        Assert.Equal(1_240_000m, await IssueValue(fx, await Iss(12, D(3, 3))));        // all of A + 2/10 of B

        var invA = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "A1", new InvoiceLineInput(105_000, a.Id, 1)));
        Assert.Equal((0m, 50_000m), (Assert.Single(await fx.Inv.PriceDifferencesAsync(fx.H.Approver, invA.Id)).InventoryPart, (await fx.Inv.PriceDifferencesAsync(fx.H.Approver, invA.Id))[0].CogsPart));
        await fx.Inv.PostAsync(fx.H.Approver, invA.Id, acknowledgePriceDifferences: true);   // layer A fully issued → all to COGS

        var invB = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "B1", new InvoiceLineInput(115_000, b.Id, 1)));
        var wb = Assert.Single(await fx.Inv.PriceDifferencesAsync(fx.H.Approver, invB.Id));
        Assert.Equal((-50_000m, 8m, -40_000m, -10_000m), (wb.Difference, wb.QuantityInStock, wb.InventoryPart, wb.CogsPart));
        await fx.Inv.PostAsync(fx.H.Approver, invB.Id, acknowledgePriceDifferences: true);

        Assert.Equal(920_000m, await IssueValue(fx, await Iss(8, D(3, 6))));          // 960,000 − 40,000 left on layer B
        Assert.Equal(0m, await fx.Bal("INV"));
        Assert.Equal(1_240_000m + 50_000m - 10_000m + 920_000m, await fx.Bal("COGS"));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
    }

    [Fact]
    public async Task SupplyToPayment_Handoff_AcceptanceIsNotAccounting_ThenOrder_ThenPaid()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);   // posting rules for the bank payment
        var voucherCount = () => s.Db.Vouchers.CountAsync();

        var inv = await PostedPurchaseOfAdditiveAsync(fx);   // payable 5,500,000 (5,000,000 + VAT 10%)
        var queue = await s.PaymentAcceptances.PayableQueueAsync(h.Auditor, D(3, 31));
        var row = Assert.Single(queue);
        Assert.Equal((5_500_000m, D(4, 2), true, "NOT_ACCEPTED"), (row.Open, row.DueDate, row.HasReceiptLink, row.Stage));

        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Auditor, inv.Id, null)));
        var before = await voucherCount();
        var acc = await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, "Đã đối chiếu đơn - phiếu nhập - hóa đơn");
        Assert.Equal((Hak.Domain.Cycles.PaymentAcceptanceStatus.Pending, 5_500_000m, null), (acc.Status, acc.Amount, acc.PaymentOrderId));
        Assert.Equal(before, await voucherCount());   // chấp nhận thanh toán KHÔNG phát sinh bút toán
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Approver, inv.Id, null)));

        // Without BANK_PAYMENT_DEFAULT the handoff waits in the payment inbox, sorted by due date.
        var inbox = Assert.Single(await s.PaymentAcceptances.InboxAsync(h.Auditor));
        Assert.Equal(("AWAITING_ORDER", 5_500_000m), (inbox.Stage, inbox.Amount));

        // The payer rejects with a reason (back to procurement), procurement accepts again.
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => s.PaymentAcceptances.RejectAsync(h.Preparer, acc.Id, " ")));
        await s.PaymentAcceptances.RejectAsync(h.Preparer, acc.Id, "Thiếu hồ sơ kiểm nghiệm");
        Assert.Empty(await s.PaymentAcceptances.InboxAsync(h.Auditor));
        acc = await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null);

        var po = await s.PaymentAcceptances.CreateOrderAsync(h.Preparer, acc.Id, h.Acc["BANK"]);
        Assert.Equal((Hak.Domain.Banking.PaymentOrderStatus.Created, 5_500_000m), (po.Status, po.Amount));
        Assert.Equal(po.Id, Assert.Single(await s.PaymentAcceptances.InboxAsync(h.Auditor)).PaymentOrderId);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.PaymentAcceptances.CreateOrderAsync(h.Preparer, acc.Id, h.Acc["BANK"])));
        Assert.Equal("ORDER_CREATED", (await s.PaymentAcceptances.PayableQueueAsync(h.Auditor, D(3, 31))).Single().Stage);

        // Normal payment-order workflow (separation of duties inside the payment cycle), then the voucher is accounted.
        await s.PaymentOrders.ApproveAsync(h.Approver, po.Id);
        await s.PaymentOrders.MarkSentAsync(h.Preparer, po.Id);
        Assert.Equal("SENT_TO_BANK", (await s.PaymentAcceptances.InboxAsync(h.Auditor)).Single().Stage);
        var (_, voucher) = await s.PaymentOrders.MarkExecutedAsync(h.Preparer, po.Id, D(3, 20), "FT26079XYZ");
        await s.Engine.SubmitAsync(h.Preparer, voucher.Id);
        await s.Engine.ApproveAsync(h.Approver, voucher.Id);
        await s.Engine.PostAsync(h.Approver, voucher.Id);

        h.Clock.UtcNow = new DateTime(2026, 3, 31, 8, 0, 0, DateTimeKind.Utc);   // "today" is after the payment date
        Assert.Empty(await s.PaymentAcceptances.PayableQueueAsync(h.Auditor, D(3, 31)));   // payable settled: derived, not stored
        var done = Assert.Single(await s.PaymentAcceptances.InboxAsync(h.Auditor));
        Assert.Equal(("PAID", 0m), (done.Stage, done.Open));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null)));   // already accepted and paid
    }

    [Fact]
    public async Task SupplyToPayment_WithDefaultBank_PaymentCycleCreatesTheOrderByItself_AndCancelFollows()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        await s.Rules.SetRoleMappingAsync(h.Admin, "BANK_PAYMENT_DEFAULT", "BANK");
        var inv = await PostedPurchaseOfAdditiveAsync(fx, "0000301");

        var acc = await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null);
        Assert.Equal(Hak.Domain.Cycles.PaymentAcceptanceStatus.OrderCreated, acc.Status);
        var po = await s.Db.PaymentOrders.SingleAsync(o => o.Id == acc.PaymentOrderId);
        Assert.Equal((Hak.Domain.Banking.PaymentOrderStatus.Created, 5_500_000m, acc.SettleJournalLineId, h.Acc["BANK"]), (po.Status, po.Amount, po.SettleJournalLineId, po.BankAccountId));
        Assert.Contains("1C26TAA/0000301", po.Description);

        await s.PaymentAcceptances.CancelAsync(h.Preparer, acc.Id, "Hoãn thanh toán theo thỏa thuận");
        Assert.Equal(Hak.Domain.Banking.PaymentOrderStatus.Cancelled, (await s.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == po.Id)).Status);
        Assert.Equal("NOT_ACCEPTED", (await s.PaymentAcceptances.PayableQueueAsync(h.Auditor, D(3, 31))).Single().Stage);
        await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null);   // can be accepted again after cancelling
    }

    [Fact]
    public async Task SalesOrder_CreditCheck_BlocksOverLimit_UnlessAuthorisedWithReason_AndShortageNeverBlocks()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await s.Setup.UpdatePartnerCommercialAsync(h.Admin, h.Partner["KH01"], "12 Lê Duẩn, Đà Nẵng", "0236 111 222", "kh01@example.vn", 10_000_000, 30);
        // KH01 already owes 8,000,000 (posted receivable).
        await h.PostedAsync(s, new Hak.Application.Accounting.VoucherInput(Hak.Domain.Vouchers.VoucherTypes.General, D(3, 1), D(3, 1), "Bán chịu",
            [new(h.Acc["AR"], Hak.Domain.Vouchers.Side.Debit, 8_000_000, h.Partner["KH01"], DueDate: D(3, 10)), new(h.Acc["REV"], Hak.Domain.Vouchers.Side.Credit, 8_000_000)]));

        var orders = Orders(fx);
        var line = new OrderLineInput(fx.Add, 10, 300_000, VatRate: 10);   // 3,300,000 incl. VAT → 11,300,000 > 10,000,000
        var order = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 5), "Bán phụ gia", [line]));

        var checker = new Hak.Application.Cycles.SalesOrderCheckService(s.Db, s.Settlement, h.Clock);
        var check = await checker.CheckAsync(h.Approver, order.Id);
        Assert.Equal((10_000_000m, 8_000_000m, 8_000_000m, 3_300_000m, 11_300_000m, true, true), (check.CreditLimit, check.CurrentBalance, check.OverdueAmount, check.OrderValue, check.BalanceAfter, check.ExceedsCredit, check.AnyShortage));
        Assert.Equal((10m, 0m, 10m), (check.Lines[0].Ordered, check.Lines[0].OnHand, check.Lines[0].Shortfall));

        Assert.Equal(ErrorCodes.CreditLimitExceeded, await CodeOf(() => orders.ConfirmAsync(h.Approver, order.Id)));
        Assert.Equal(ErrorCodes.CreditLimitExceeded, await CodeOf(() => orders.ConfirmAsync(h.Approver, order.Id, "  ")));
        var noOverride = h.Approver with { Permissions = h.Approver.Permissions.Where(p => p != "credit.override").ToHashSet() };
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => orders.ConfirmAsync(noOverride, order.Id, "Khách lâu năm, đã trả đủ kỳ trước")));
        Assert.Equal(OrderStatus.Draft, (await s.Db.TradeOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);   // failed attempts changed nothing
        var confirmed = await orders.ConfirmAsync(h.Approver, order.Id, "Khách lâu năm, đã trả đủ kỳ trước");
        Assert.Equal(OrderStatus.Confirmed, confirmed.Status);
        Assert.Contains(s.Db.AuditLogs.AsNoTracking().Select(a => a.Action).ToList(), a => a == "order.credit_override");

        // A customer without a declared limit is not checked; missing stock never blocks the order.
        var free = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH02"], D(3, 5), "Bán không hạn mức", [new OrderLineInput(fx.Add, 999, 1_000_000)]));
        var freeCheck = await checker.CheckAsync(h.Approver, free.Id);
        Assert.False(freeCheck.ExceedsCredit);
        Assert.True(freeCheck.AnyShortage);
        Assert.Equal(OrderStatus.Confirmed, (await orders.ConfirmAsync(h.Approver, free.Id)).Status);
    }

    [Fact]
    public async Task ReorderList_ShowsItemsBelowMinimumStock_WithOptimalQuantity()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await s.Stock.UpdateItemCommercialAsync(h.Admin, fx.Add, 60_000, 10, 50, 200);
        var reorder = new Hak.Application.Cycles.ReorderService(s.Db);

        var row = Assert.Single(await reorder.ListAsync(h.Auditor));
        Assert.Equal(("PHU", 0m, 50m, 200m), (row.Code, row.OnHand, row.MinStock, row.SuggestedQty));

        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 2), fx.Add);   // 100 kg ≥ minimum 50
        Assert.Empty(await reorder.ListAsync(h.Auditor));
        await PostedIssueAsync(fx, 70, D(3, 4), fx.Add);                 // 30 kg left < 50
        Assert.Equal(30m, Assert.Single(await reorder.ListAsync(h.Auditor)).OnHand);

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Stock.UpdateItemCommercialAsync(h.Admin, fx.Add, -1, null, null, null)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Stock.UpdateItemCommercialAsync(h.Auditor, fx.Add, 1, null, null, null)));
    }

    [Fact]
    public async Task Supplier_Approval_Rating_And_DraftOrderFromReorderList()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        var suppliers = new Hak.Application.Cycles.SupplierService(s.Db, Orders(fx), s.Audit, h.Clock);
        var inspections = new Hak.Application.Cycles.GoodsInspectionService(s.Db, s.Audit, h.Clock);
        var reorder = new Hak.Application.Cycles.ReorderService(s.Db);
        await s.Stock.UpdateItemCommercialAsync(h.Admin, fx.Add, 60_000, 10, 50, 200);

        // Danh mục cần đặt → đơn mua Nháp (chưa có đơn nào thì chưa có giá gợi ý).
        Assert.Null(Assert.Single(await reorder.ListAsync(h.Auditor)).LastPrice);
        var draft = await suppliers.CreateOrderFromReorderAsync(h.Preparer, h.Partner["NCC01"], [new(fx.Add, 200, 55_000)], D(3, 10));
        Assert.Equal((OrderStatus.Draft, InvoiceKind.Purchase), (draft.Status, draft.Kind));
        var hint = Assert.Single(await reorder.ListAsync(h.Auditor));
        Assert.Equal((55_000m, h.Partner["NCC01"]), (hint.LastPrice, hint.LastPartnerId));
        Assert.Equal(ErrorCodes.VoucherNoLines, await CodeOf(() => suppliers.CreateOrderFromReorderAsync(h.Preparer, h.Partner["NCC01"], [])));

        // Nhập đúng hạn 200 kg theo đơn, kiểm nghiệm đạt 180 → tỷ lệ đạt 90%, giao đúng hạn 100% → điểm 94 hạng A.
        await Orders(fx).ConfirmAsync(h.Approver, draft.Id);
        var receipt = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 8), D(3, 8), "Nhập theo đơn",
            [new StockLineInput(fx.Add, 200, 11_000_000, fx.A("GRNI"), OrderId: draft.Id, OrderLineNo: 1)], PartnerId: h.Partner["NCC01"]));
        await s.Stock.PostDocumentAsync(h.Approver, receipt.Id);
        await inspections.CreateAsync(h.Preparer, new Hak.Application.Cycles.InspectionInput(receipt.Id, D(3, 8), "Thủ kho", null, [new(1, 180)]));
        var row = Assert.Single(await suppliers.RatingAsync(h.Auditor, D(3, 1), D(3, 31)), r => r.Code == "NCC01");
        Assert.Equal((1, 90m, 1, 100m, 94, "A"), (row.Receipts, row.AcceptRate, row.OnTimeReceipts, row.OnTimeRate, row.Score, row.Grade));
        Assert.Equal(11_000_000m, row.PurchaseAmount);

        // Công ty bật duyệt nhà cung cấp: NCC mới chờ duyệt, chưa đặt hàng được; người tạo không tự duyệt; chặn cần lý do.
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => suppliers.SetApprovalPolicyAsync(h.Auditor, true)));
        await suppliers.SetApprovalPolicyAsync(h.Admin, true);
        var ncc2 = (await s.Setup.CreatePartnerAsync(h.Admin, "NCC02", "Nhà cung cấp 2", PartnerRoles.Supplier)).Id;
        var kh = (await s.Setup.CreatePartnerAsync(h.Admin, "KH09", "Khách 9", PartnerRoles.Customer)).Id;
        Assert.Equal(ErrorCodes.SupplierNotApproved, await CodeOf(() => suppliers.CreateOrderFromReorderAsync(h.Preparer, ncc2, [new(fx.Add, 10, 1)])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => suppliers.SetStatusAsync(h.Admin, kh, SupplierStatus.Blocked, "x")));   // khách hàng không phải NCC
        await suppliers.SetStatusAsync(h.Approver, ncc2, SupplierStatus.Approved, null);
        await suppliers.CreateOrderFromReorderAsync(h.Preparer, ncc2, [new(fx.Add, 10, 1)]);
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => suppliers.SetStatusAsync(h.Admin, ncc2, SupplierStatus.Blocked, " ")));
        await suppliers.SetStatusAsync(h.Admin, ncc2, SupplierStatus.Blocked, "Giao hàng kém chất lượng");
        Assert.Equal(ErrorCodes.SupplierNotApproved, await CodeOf(() => suppliers.CreateOrderFromReorderAsync(h.Preparer, ncc2, [new(fx.Add, 10, 1)])));
        Assert.Contains(await suppliers.RatingAsync(h.Auditor, D(3, 1), D(3, 31)), r => r.Code == "NCC02" && r.Status == SupplierStatus.Blocked && r.Score == null);
    }

    [Fact]
    public async Task HomeWorkQueue_CountsWaitingWorkPerScreen()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        var home = new Hak.Application.Cycles.HomeService(s.Db, s.PaymentAcceptances, new Hak.Application.Cycles.SalesReportService(s.Db, s.Settlement),
            new Hak.Application.Cycles.PayrollPaymentService(s.Db, s.Settlement, s.PaymentOrders, s.Audit), new Hak.Application.Cycles.ReorderService(s.Db), new Hak.Application.Loans.LoanScheduleService(s.Db, s.Audit, h.Clock), h.Clock, new ContractServicePlanService(s.Db, s.Audit, h.Clock));
        Assert.Empty(await home.WorkQueueAsync(h.Auditor));

        await DraftReceiptAsync(fx, 10, D(3, 2));                                           // 1 phiếu kho nháp
        await s.Stock.UpdateItemCommercialAsync(h.Admin, fx.Add, null, null, 50, 200);     // PHU dưới tồn tối thiểu
        await Orders(fx).CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 2), "Đơn bán", [new OrderLineInput(fx.Dam, 1, 1000)]));
        var q = (await home.WorkQueueAsync(h.Auditor)).ToDictionary(w => w.Screen);
        Assert.Equal(1, q["stock"].Count);
        Assert.Equal(1, q["orders"].Count);
        Assert.Equal(1, q["procurement"].Count);
        Assert.Contains("dưới tồn tối thiểu", q["procurement"].Hint);
    }

    [Fact]
    public async Task ItemUnits_FixedAlternateUnit_ExclusiveWithDrynessBdmt()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        // Đơn vị quy đổi cố định: 1 tấn = 1000 kg (PHU tính bằng kg); số lượng trên sổ vẫn giữ theo đơn vị chính.
        var item = await s.Stock.SetItemAltUnitAsync(h.Admin, fx.Add, " tấn ", 1000);
        Assert.Equal(("tấn", 1000m), (item.AltUnit, item.AltFactor));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Stock.SetItemAltUnitAsync(h.Auditor, fx.Add, "tấn", 1000)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Stock.SetItemAltUnitAsync(h.Admin, fx.Add, "tấn", 0)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Stock.SetItemAltUnitAsync(h.Admin, fx.Add, "tấn", null)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Stock.SetItemAltUnitAsync(h.Admin, fx.Add, "kg", 2)));          // trùng đơn vị chính

        // Dăm gỗ theo dõi độ khô: đơn vị quy đổi là BDMT theo từng thời điểm, không khai báo hệ số cố định; và ngược lại.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Stock.SetItemAltUnitAsync(h.Admin, fx.Dam, "BDMT", 1)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Dryness.SetItemTracksDrynessAsync(h.Admin, fx.Add, true)));
        await s.Stock.SetItemAltUnitAsync(h.Admin, fx.Add, null, null);                                                                 // xóa đơn vị quy đổi
        Assert.Null((await s.Db.Items.AsNoTracking().FirstAsync(i => i.Id == fx.Add)).AltUnit);
        await s.Dryness.SetItemTracksDrynessAsync(h.Admin, fx.Add, true);

        Assert.Equal(2m, UnitConversion.ToAlt(2_000, 1000));
        Assert.Equal(12_000m, UnitConversion.ToMain(12, 1000));
        Assert.Equal(0.3333m, UnitConversion.ToAlt(1, 3));
    }

    [Fact]
    public async Task RevenueCycle_Order_Dispatch_Delivery_Invoice_Receivable_Reports()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);   // receipt rules for the collection report
        await s.Setup.UpdatePartnerCommercialAsync(h.Admin, h.Partner["KH01"], "12 Lê Duẩn", null, null, null, 30);
        await s.Stock.UpdateItemCommercialAsync(h.Admin, fx.Add, 60_000, 10, null, null);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var svc = new Hak.Application.Cycles.SalesDispatchService(s.Db, s.Stock, fx.Inv, s.Audit, h.Clock);
        var reports = new Hak.Application.Cycles.SalesReportService(s.Db, s.Settlement);
        var orders = Orders(fx);

        // Order of 40 kg at 60,000 without VAT on the line: VAT falls back to the item's default (10%).
        var order = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 5), "Bán phụ gia", [new OrderLineInput(fx.Add, 40, 60_000)]));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CreateDispatchAsync(h.Preparer, new(order.Id, fx.Wh, D(3, 6), null, null, [new(1, 30)]))));   // order not confirmed yet
        await orders.ConfirmAsync(h.Approver, order.Id);

        // Lệnh bán hàng: no accounting; cannot exceed the order; must be issued before delivery.
        var dispatch = await svc.CreateDispatchAsync(h.Preparer, new(order.Id, fx.Wh, D(3, 6), "Kho khách A", "Giao trong tuần", [new(1, 30)]));
        Assert.StartsWith("LBH2026-", dispatch.Number);
        Assert.Equal(ErrorCodes.OverDelivered, await CodeOf(() => svc.CreateDispatchAsync(h.Preparer, new(order.Id, fx.Wh, D(3, 6), null, null, [new(1, 11)]))));   // 30 + 11 > 40
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CreateDeliveryAsync(h.Preparer, new(dispatch.Id, D(3, 7), null, null, null, null, null, [new(1, 10)]))));
        await svc.IssueAsync(h.Preparer, dispatch.Id);

        // Phiếu giao hàng → phiếu xuất kho Nháp linked to the order line (no ledger entry yet).
        var vouchersBefore = await s.Db.Vouchers.CountAsync();
        var d1 = await svc.CreateDeliveryAsync(h.Preparer, new(dispatch.Id, D(3, 7), null, "Xe 43C-123.45", "Nguyễn Văn A", "VD0001", null, [new(1, 20)]));
        Assert.StartsWith("PGH2026-", d1.Number);
        Assert.Equal("Kho khách A", d1.ShipTo);   // defaults to the dispatch order's destination
        Assert.Equal(vouchersBefore, await s.Db.Vouchers.CountAsync());
        var doc1 = await s.Db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == d1.StockDocumentId);
        Assert.Equal((StockDocumentType.Issue, StockDocumentStatus.Draft, order.Id, 1, 20m), (doc1.Type, doc1.Status, doc1.Lines[0].OrderId, doc1.Lines[0].OrderLineNo, doc1.Lines[0].Quantity));
        Assert.Equal(ErrorCodes.OverDelivered, await CodeOf(() => svc.CreateDeliveryAsync(h.Preparer, new(dispatch.Id, D(3, 8), null, null, null, null, null, [new(1, 11)]))));   // 20 + 11 > 30
        var d2 = await svc.CreateDeliveryAsync(h.Preparer, new(dispatch.Id, D(3, 8), null, null, null, null, null, [new(1, 10)]));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CancelDispatchAsync(h.Preparer, dispatch.Id, "đổi ý")));   // has deliveries

        // Hóa đơn needs the stock issue posted first (that is when goods leave and the Nhật ký is written).
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CreateInvoiceAsync(h.Preparer, d1.Id, "1C26TAA", "0000900", D(3, 20))));
        await s.Stock.PostDocumentAsync(h.Approver, d1.StockDocumentId);
        var inv = await svc.CreateInvoiceAsync(h.Preparer, d1.Id, "1C26TAA", "0000900", D(3, 20));
        var line = Assert.Single(inv.Lines);
        Assert.Equal((60_000m, 10m, 20m, D(4, 19), order.Id), (line.UnitPrice, line.VatRate, line.StockQuantity, inv.DueDate, line.OrderId));   // price/VAT from the order and catalogue, due = date + 30 days
        await fx.Inv.PostAsync(h.Approver, inv.Id);
        Assert.Equal(1_320_000m, await fx.Bal("AR", h.Partner["KH01"]));

        var view = Assert.Single(await svc.ListAsync(h.Auditor, order.Id));
        Assert.Equal((30m, 30m, 0m), (view.Lines[0].Quantity, view.Lines[0].Delivered, view.Lines[0].Outstanding));
        Assert.Equal("INVOICED", view.Deliveries.Single(x => x.Id == d1.Id).Stage);
        Assert.Equal(inv.Number, view.Deliveries.Single(x => x.Id == d1.Id).InvoiceNumber);
        Assert.Equal("AWAITING_STOCK_POSTING", view.Deliveries.Single(x => x.Id == d2.Id).Stage);

        // Cancelling: a posted issue must be reversed first; a draft one is cancelled with its stock document.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CancelDeliveryAsync(h.Preparer, d1.Id, "nhầm")));
        await svc.CancelDeliveryAsync(h.Preparer, d2.Id, "Khách hoãn nhận");
        Assert.Equal(StockDocumentStatus.Cancelled, (await s.Db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == d2.StockDocumentId)).Status);
        Assert.Equal(10m, (await svc.ListAsync(h.Auditor, order.Id)).Single().Lines[0].Outstanding);

        // Hàng chờ thu tiền theo ngày đáo hạn, báo cáo bán hàng và thu tiền.
        var q = Assert.Single(await reports.ReceivableQueueAsync(h.Auditor, D(3, 31)));
        Assert.Equal((1_320_000m, D(4, 19), 0), (q.Open, q.DueDate, q.DaysOverdue));
        Assert.Equal(12, Assert.Single(await reports.ReceivableQueueAsync(h.Auditor, D(5, 1))).DaysOverdue);
        var sales = await reports.AnalyseAsync(h.Auditor, D(1, 1), D(12, 31));
        var byItem = Assert.Single(sales.ByItem);
        Assert.Equal(("PHU", 20m, 1_200_000m, 120_000m), (byItem.Key, byItem.Quantity, byItem.Revenue, byItem.Vat));
        Assert.Equal((1_000_000m, 0), (byItem.Cost, byItem.UncostedLines));   // 20 kg × 50,000 bình quân
        Assert.Equal((1_000_000m, 0), (sales.TotalCost, sales.UncostedLines));
        Assert.Equal(1_200_000m, Assert.Single(sales.ByPartner).Revenue);
        Assert.Equal("2026-03", Assert.Single(sales.ByMonth).Key);
        await Phase2Fixture.PostDocAsync(h, s, Phase2Fixture.Receipt(h, 500_000, D(3, 25)));
        var coll = await reports.CollectionsAsync(h.Auditor, D(3, 1), D(3, 31));
        Assert.Equal((500_000m, "KH01"), (coll.Total, Assert.Single(coll.ByPartner).PartnerCode));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.CreateDispatchAsync(h.Auditor, new(order.Id, fx.Wh, D(3, 6), null, null, [new(1, 1)]))));
    }

    [Fact]
    public async Task PurchaseMatch_InspectionAndPriceMismatch_BlockAcceptance_UnlessAcknowledgedWithReason()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var orders = Orders(fx);
        var inspections = new Hak.Application.Cycles.GoodsInspectionService(s.Db, s.Audit, h.Clock);
        var match = new Hak.Application.Cycles.PurchaseMatchService(s.Db);

        // Order 100 kg at 50,000; receipt linked to the order line; invoice at the order price.
        var order = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Purchase, h.Partner["NCC01"], D(3, 1), "Mua phụ gia", [new OrderLineInput(fx.Add, 100, 50_000)]));
        await orders.ConfirmAsync(h.Approver, order.Id);
        var receipt = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 2), D(3, 2), "Nhập theo đơn",
            [new StockLineInput(fx.Add, 100, 0, fx.A("GRNI"), OrderId: order.Id, OrderLineNo: 1)], PartnerId: h.Partner["NCC01"]));
        var inv = await fx.Inv.CreateAsync(h.Preparer, Purchase(fx, "0000400", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        await fx.Inv.PostAsync(h.Approver, inv.Id);

        // No inspection yet: information only, not blocking; the receipt is listed as pending inspection.
        var m0 = await match.MatchAsync(h.Auditor, inv.Id);
        Assert.True(m0.Matched);
        Assert.Contains(m0.Flags, f => f.Code == "NO_INSPECTION" && !f.Blocking);
        Assert.Equal(receipt.Id, Assert.Single(await inspections.ReceiptsAsync(h.Auditor, onlyPending: true), r => r.StockDocumentId == receipt.Id).StockDocumentId);

        // Inspection accepts 90 of 100 kg: invoice for 100 now exceeds what was accepted.
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => inspections.CreateAsync(h.Auditor, new(receipt.Id, D(3, 2), "Ban kiểm nghiệm", null))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 2), "Ban kiểm nghiệm", null, [new(1, 101)]))));
        var bb = await inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 2), "Ban kiểm nghiệm số 1", "10 kg ẩm", [new(1, 90, "10 kg không đạt")]));
        Assert.StartsWith("BBKN2026-", bb.Number);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 2), "Ban khác", null))));
        var row = Assert.Single(await inspections.ReceiptsAsync(h.Auditor), r => r.StockDocumentId == receipt.Id);
        Assert.Equal((100m, 90m, 10m), (row.Inspection!.Lines[0].ReceivedQty, row.Inspection.Lines[0].AcceptedQty, row.Inspection.Lines[0].RejectedQty));

        var m1 = await match.MatchAsync(h.Auditor, inv.Id);
        Assert.False(m1.Matched);
        Assert.Equal(("OVER_ACCEPTED", 100m, 90m), (m1.Flags.Single(f => f.Blocking).Code, m1.Lines[0].InvoicedQty, m1.Lines[0].AcceptedQty));
        Assert.Equal(1, Assert.Single(await new Hak.Application.Cycles.PaymentAcceptanceService(s.Db, s.PaymentOrders, s.Settlement, s.Audit, h.Clock).PayableQueueAsync(h.Auditor, D(3, 31))).MismatchCount);

        // Acceptance is refused; acknowledging needs a reason, is audited, and then goes through.
        Assert.Equal(ErrorCodes.PaymentMatchFailed, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, "ok")));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, "  ", acknowledgeMismatch: true)));
        var acc = await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, "Đã thỏa thuận với NCC trả 10 kg ẩm bằng giảm giá kỳ sau", acknowledgeMismatch: true);
        Assert.Equal(Hak.Domain.Cycles.PaymentAcceptanceStatus.Pending, acc.Status);
        Assert.Contains(s.Db.AuditLogs.AsNoTracking().Where(a => a.Action == "payment_acceptance.accept").Select(a => a.DataJson).ToList(), j => j!.Contains("mismatchAcknowledged\":true") || j.Contains("\"mismatchAcknowledged\":true"));

        // Cancelling the inspection lets it be redone; a price different from the order is also blocking.
        await inspections.CancelAsync(h.Preparer, bb.Id, "Lập lại");
        Assert.True((await match.MatchAsync(h.Auditor, inv.Id)).Matched);
        var order2 = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Purchase, h.Partner["NCC01"], D(3, 3), "Mua phụ gia lô 2", [new OrderLineInput(fx.Add, 10, 40_000)]));
        await orders.ConfirmAsync(h.Approver, order2.Id);
        var receipt2 = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 4), D(3, 4), "Nhập lô 2",
            [new StockLineInput(fx.Add, 10, 0, fx.A("GRNI"), OrderId: order2.Id, OrderLineNo: 1)], PartnerId: h.Partner["NCC01"]));
        var inv2 = await fx.Inv.CreateAsync(h.Preparer, Purchase(fx, "0000401", new InvoiceLineInput(50_000, receipt2.Id, 1, VatRate: 10)));
        await fx.Inv.PostAsync(h.Approver, inv2.Id);
        var m2 = await match.MatchAsync(h.Auditor, inv2.Id);
        Assert.Equal(("PRICE_DIFF", 50_000m, 40_000m), (m2.Flags.Single(f => f.Blocking).Code, m2.Lines[0].UnitPrice, m2.Lines[0].OrderPrice));
        Assert.Equal(ErrorCodes.PaymentMatchFailed, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv2.Id, null)));
    }

    [Fact]
    public async Task CashPlan_BucketsReceivablesAndPayablesByDueDate_AndOverdueNoticesPerCustomer()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        await s.Setup.UpdatePartnerCommercialAsync(h.Admin, h.Partner["KH01"], "12 Lê Duẩn", "0236 111 222", null, null, null);
        await Phase2Fixture.PostDocAsync(h, s, Phase2Fixture.Receipt(h, 10_000_000, D(3, 1)));   // opening cash 10,000,000 on the cash account

        // Payable 5,500,000 due 02/04 (purchase) and receivable 660,000 due 10/04 (sale of 10 kg at 60,000 + 10% VAT).
        var purchase = await PostedPurchaseOfAdditiveAsync(fx);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Dam);
        var issue = await PostedIssueAsync(fx, 10, D(3, 12), fx.Add);
        var sale = await fx.Inv.CreateAsync(h.Preparer, new InvoiceInput(InvoiceKind.Sales, h.Partner["KH01"], "1C26T", "200", D(3, 12), D(3, 12), "Bán phụ gia",
            [new InvoiceLineInput(60_000, issue.Id, 1, VatRate: 10)], DueDate: D(4, 10)));
        await fx.Inv.PostAsync(h.Approver, sale.Id);

        var plan = new Hak.Application.Cycles.CashPlanService(s.Db, s.Ledger, new Hak.Application.Cycles.SalesReportService(s.Db, s.Settlement), s.PaymentAcceptances, new Hak.Application.Loans.LoanScheduleService(s.Db, s.Audit, h.Clock));
        var p = await plan.PlanAsync(h.Auditor, D(3, 31), 4);
        Assert.Equal(10_000_000m, p.OpeningCash);
        Assert.Equal(7, p.Buckets.Count);   // overdue/today + 4 weeks + later + no due date
        var w1 = p.Buckets[1]; var w2 = p.Buckets[2];
        Assert.Equal((D(4, 1), D(4, 7), 0m, 5_500_000m, 0m, 4_500_000m), (w1.From, w1.To, w1.Inflow, w1.Outflow, w1.OutflowAccepted, w1.ClosingCash));
        Assert.Equal((D(4, 8), D(4, 14), 660_000m, 0m, 5_160_000m), (w2.From, w2.To, w2.Inflow, w2.Outflow, w2.ClosingCash));
        Assert.Equal((660_000m, 5_500_000m, 5_160_000m), (p.TotalInflow, p.TotalOutflow, p.EndingCash));

        // Accepting the payable moves it to the "accepted" part of the outflow.
        await s.PaymentAcceptances.AcceptAsync(h.Preparer, purchase.Id, null);
        Assert.Equal(5_500_000m, (await plan.PlanAsync(h.Auditor, D(3, 31), 4)).Buckets[1].OutflowAccepted);

        // As of 01/05 the sale is 21 days overdue and appears in the first bucket and in the overdue notice (with the customer's contact details).
        Assert.Equal(660_000m, (await plan.PlanAsync(h.Auditor, D(5, 1), 2)).Buckets[0].Inflow);
        var reports = new Hak.Application.Cycles.SalesReportService(s.Db, s.Settlement);
        var notice = Assert.Single(await reports.OverdueNoticesAsync(h.Auditor, D(5, 1)));
        Assert.Equal(("KH01", "12 Lê Duẩn", 660_000m, 21), (notice.PartnerCode, notice.Address, notice.TotalOpen, notice.MaxDaysOverdue));
        Assert.Equal(D(4, 10), Assert.Single(notice.Invoices).DueDate);
        Assert.Empty(await reports.OverdueNoticesAsync(h.Auditor, D(4, 10)));   // due today is not overdue yet
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => plan.PlanAsync(h.Auditor, D(3, 31), 0)));
    }

    [Fact]
    public async Task QualityCheck_BlocksPostingOfSupplierReceipts_UntilFullyAcceptedInspection()
    {
        using var fx = await SetupAsync();
        var insp = new Hak.Application.Cycles.GoodsInspectionService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        await fx.S.Stock.SetItemRequiresQcAsync(fx.H.Admin, fx.Add, true);
        var doc = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 2), D(3, 2), "Nhập phụ gia",
            [new StockLineInput(fx.Add, 100, 5_000_000, fx.A("GRNI"))], PartnerId: fx.H.Partner["NCC01"]));
        Assert.Equal(ErrorCodes.QualityCheckRequired, await CodeOf(() => fx.S.Stock.PostDocumentAsync(fx.H.Approver, doc.Id)));         // chưa có biên bản
        var bad = await insp.CreateAsync(fx.H.Preparer, new InspectionInput(doc.Id, D(3, 2), "Ban kiểm nghiệm", "Lô lẫn tạp chất", [new InspectionLineInput(1, 80)]));
        Assert.Equal(ErrorCodes.QualityCheckRequired, await CodeOf(() => fx.S.Stock.PostDocumentAsync(fx.H.Approver, doc.Id)));         // chỉ đạt 80/100
        await insp.CancelAsync(fx.H.Preparer, bad.Id, "Lập lại sau tuyển chọn");
        await insp.CreateAsync(fx.H.Preparer, new InspectionInput(doc.Id, D(3, 3), "Ban kiểm nghiệm", "Đạt"));                            // không nêu dòng = đạt toàn bộ
        Assert.Equal(StockDocumentStatus.Posted, (await fx.S.Stock.PostDocumentAsync(fx.H.Approver, doc.Id)).Status);
        // Vật tư không bắt buộc, hoặc phiếu không có đối tác (nhập thành phẩm, tồn đầu kỳ) thì không bị chặn.
        await PostedReceiptAsync(fx, 10, 15_000_000, D(3, 4), fx.Dam);
        await fx.S.Stock.SetItemRequiresQcAsync(fx.H.Admin, fx.Dam, true);
        var own = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 5), D(3, 5), "Nhập thành phẩm",
            [new StockLineInput(fx.Dam, 5, 7_000_000, fx.A("COGS"))]));
        Assert.Equal(StockDocumentStatus.Posted, (await fx.S.Stock.PostDocumentAsync(fx.H.Approver, own.Id)).Status);
    }

    [Fact]
    public async Task SalesAdjustmentApproval_PolicyLimitsReturnsAndDiscountsToApprovedScope()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);
        var appr = new SalesApprovalService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        fx.H.Clock.UtcNow = new DateTime(2026, 3, 14, 8, 0, 0, DateTimeKind.Utc);
        // Chưa bật chính sách: đường cũ vẫn chạy.
        Assert.False(await appr.PolicyAsync(fx.H.Auditor));
        await appr.SetPolicyAsync(fx.H.Approver, true);
        Assert.Equal(ErrorCodes.ApprovalRequired, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 10, D(3, 15))));
        Assert.Equal(ErrorCodes.ApprovalRequired, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Discount, "1C26T", "102", D(3, 20), D(3, 20), "Giảm giá", [new AdjustmentLineInput(1, Amount: 1_000_000)]))));

        // Yêu cầu: lý do bắt buộc, số lượng không vượt số đã bán, người lập không tự duyệt.
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => appr.RequestAsync(fx.H.Preparer, new(inv.Id, AdjustmentKind.Return, " ", [new(1, 10, null)]))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => appr.RequestAsync(fx.H.Preparer, new(inv.Id, AdjustmentKind.Return, "Hàng lỗi", [new(1, 61, null)]))));
        var req = await appr.RequestAsync(fx.H.Preparer, new(inv.Id, AdjustmentKind.Return, "Độ khô không đạt hợp đồng", [new(1, 10, null)]));
        var own = await appr.RequestAsync(fx.H.Approver, new(inv.Id, AdjustmentKind.Return, "Tự lập", [new(1, 5, null)]));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => appr.DecideAsync(fx.H.Approver, own.Id, true, null)));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => appr.DecideAsync(fx.H.Approver, req.Id, false, " ")));
        Assert.Equal(ErrorCodes.ApprovalRequired, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 10, D(3, 15))));   // chưa duyệt vẫn chặn
        await appr.DecideAsync(fx.H.Approver, req.Id, true, "Đồng ý theo biên bản giám định");

        // Trong phạm vi 10 MT thì được; vượt thì chặn; phần đã dùng hiện trên yêu cầu.
        var ret = await fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 10, D(3, 15));
        Assert.Equal(ErrorCodes.ApprovalRequired, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 1, D(3, 16))));
        var row = (await appr.RequestsAsync(fx.H.Auditor, inv.Id)).Single(r => r.Id == req.Id);
        Assert.Equal((ApprovalStatus.Approved, 10m), (row.Status, row.Lines[0].Used));
        _ = ret;

        // Giảm giá: duyệt 3 triệu; lập 3 triệu được, thêm 1 đồng thì chặn.
        var dreq = await appr.RequestAsync(fx.H.Preparer, new(inv.Id, AdjustmentKind.Discount, "Giảm giá do độ khô thấp", [new(1, null, 3_000_000)]));
        await appr.DecideAsync(fx.H.Approver, dreq.Id, true, null);
        await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Discount, "1C26T", "102", D(3, 20), D(3, 20), "Giảm giá", [new AdjustmentLineInput(1, Amount: 3_000_000)]));
        Assert.Equal(ErrorCodes.ApprovalRequired, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Discount, "1C26T", "103", D(3, 21), D(3, 21), "Thêm", [new AdjustmentLineInput(1, Amount: 1)]))));
        // Tăng giá không bị chặn; hủy yêu cầu đã duyệt không làm mất hóa đơn đã lập nhưng không cấp thêm quyền.
        await appr.CancelAsync(fx.H.Preparer, dreq.Id, "Thu hồi");
        await appr.SetPolicyAsync(fx.H.Approver, false);
        await fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 1, D(3, 16));   // tắt chính sách thì lại tự do
    }

    [Fact]
    public async Task DebtWriteOff_Request_Approve_Voucher_UsesProvisionThenExpense_AndSettlesTheReceivable()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);                          // phải thu KH01 143.856.000
        foreach (var (code, name) in new[] { ("PROV", "Dự phòng phải thu khó đòi (thử)"), ("BDEXP", "Chi phí xóa nợ (thử)") }) fx.H.Acc[code] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, code, name)).Id;
        var appr = new SalesApprovalService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        fx.H.Clock.UtcNow = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var cands = await appr.CandidatesAsync(fx.H.Auditor, fx.H.Partner["KH01"], D(9, 1));
        var c = Assert.Single(cands);
        Assert.Equal((143_856_000m, true), (c.Open, c.OverdueDays > 100));

        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => appr.RequestWriteOffAsync(fx.H.Preparer, new(fx.H.Partner["KH01"], " ", "QĐ 12", [new(c.JournalLineId, 100)]))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => appr.RequestWriteOffAsync(fx.H.Preparer, new(fx.H.Partner["KH01"], "Khách giải thể", " ", [new(c.JournalLineId, 100)]))));
        Assert.Equal(ErrorCodes.SettlementExceedsOpen, await CodeOf(() => appr.RequestWriteOffAsync(fx.H.Preparer, new(fx.H.Partner["KH01"], "Khách giải thể", "QĐ 12", [new(c.JournalLineId, 143_856_001)]))));
        // Xóa 40 triệu, trong đó 30 triệu đã trích dự phòng.
        var w = await appr.RequestWriteOffAsync(fx.H.Preparer, new(fx.H.Partner["KH01"], "Khách giải thể, không còn tài sản", "QĐ 12/2026, biên bản đối chiếu 15/8", [new(c.JournalLineId, 40_000_000, 30_000_000)]));
        Assert.Equal(103_856_000m, Assert.Single(await appr.CandidatesAsync(fx.H.Auditor, fx.H.Partner["KH01"], D(9, 1))).Open);   // khoản đã nằm trong phiếu thì không chọn lại
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => appr.CreateWriteOffVoucherAsync(fx.H.Preparer, w.Id, D(9, 1))));  // chưa duyệt
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => appr.DecideWriteOffAsync(fx.H.Preparer, w.Id, true, null)));           // không phải người có thẩm quyền
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(async () => { var own = await appr.RequestWriteOffAsync(fx.H.Approver, new(fx.H.Partner["KH01"], "x", "y", [new(c.JournalLineId, 1)])); await appr.DecideWriteOffAsync(fx.H.Approver, own.Id, true, null); }));
        await appr.DecideWriteOffAsync(fx.H.Approver, w.Id, true, "Đồng ý");
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => appr.CreateWriteOffVoucherAsync(fx.H.Preparer, w.Id, D(9, 1))));
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "BAD_DEBT_PROVISION", "PROV");
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "BAD_DEBT_EXPENSE", "BDEXP");
        var v = await appr.CreateWriteOffVoucherAsync(fx.H.Preparer, w.Id, D(9, 1));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => appr.CreateWriteOffVoucherAsync(fx.H.Preparer, w.Id, D(9, 1))));
        await fx.S.Engine.SubmitAsync(fx.H.Preparer, v.Id); await fx.S.Engine.ApproveAsync(fx.H.Approver, v.Id); await fx.S.Engine.PostAsync(fx.H.Approver, v.Id);
        Assert.Equal(143_856_000m - 40_000_000m, await fx.Bal("AR"));
        Assert.Equal(30_000_000m, await fx.Bal("PROV"));
        Assert.Equal(10_000_000m, await fx.Bal("BDEXP"));
        var row = Assert.Single(await appr.WriteOffsAsync(fx.H.Auditor), r => r.Id == w.Id);
        Assert.True(row.Done);
        Assert.Equal(103_856_000m, Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), null, D(12, 31))).Open);
    }

    private static async Task PostVoucherAsync(Fx fx, Guid id)
    {
        await fx.S.Engine.SubmitAsync(fx.H.Preparer, id); await fx.S.Engine.ApproveAsync(fx.H.Approver, id); await fx.S.Engine.PostAsync(fx.H.Approver, id);
    }

    [Fact]
    public async Task PaymentDiscount_Sales_PartnerDefaultTerms_DiscountOnGoodsOnly_ExpiresAfterDeadline()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH01"], null, null, null, null, 30, 2m, 10);   // 2% nếu trả trong 10 ngày
        var (inv, _) = await PostedBdmtSaleAsync(fx);                                                                              // ngày HĐ 12/3, tiền hàng 133.200.000 + VAT = 143.856.000
        Assert.Equal((2m, 10), (inv.PaymentDiscountPercent, inv.PaymentDiscountDays));
        var c = Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 20)));
        Assert.Equal((143_856_000m, 2_664_000m, 141_192_000m, D(3, 22), false), (c.Open, c.Discount, c.CashToPay, c.Deadline, c.Expired));
        Assert.True(Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 25))).Expired);

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 25))));     // quá hạn hưởng
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("AR"), D(3, 20))));       // không phải TK tiền
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 20))));
        fx.H.Acc["FINEXP"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FINEXP", "Chi phí tài chính (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PAYMENT_DISCOUNT_EXPENSE", "FINEXP");
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 20));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 20))));
        Assert.True(Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 20))).VoucherPending);
        await PostVoucherAsync(fx, v.Id);
        Assert.Equal(0m, await fx.Bal("AR"));
        Assert.Equal(141_192_000m, await fx.Bal("CASH"));
        Assert.Equal(2_664_000m, await fx.Bal("FINEXP"));
        Assert.Empty(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 20)));
    }

    [Fact]
    public async Task PaymentDiscount_Purchase_ExplicitTerms_CreditsFinancialIncome_AndValidatesTerms()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        var receipt = await DraftReceiptAsync(fx, 100, D(3, 2), fx.Add);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "D1", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)) with { PaymentDiscountPercent = 1 })));   // thiếu số ngày
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "D1", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)) with { PaymentDiscountPercent = 120, PaymentDiscountDays = 10 })));
        var created = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "D2", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)) with { PaymentDiscountPercent = 1, PaymentDiscountDays = 10 });
        var inv = await fx.Inv.PostAsync(fx.H.Approver, created.Id);                                                                                                   // 5.000.000 + VAT 500.000, ngày HĐ 3/3
        var c = Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Purchase, D(3, 10)));
        Assert.Equal((5_500_000m, 50_000m, 5_450_000m), (c.Open, c.Discount, c.CashToPay));
        fx.H.Acc["FININC"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FININC", "Doanh thu tài chính (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PAYMENT_DISCOUNT_INCOME", "FININC");
        await fx.H.PostedAsync(fx.S, new VoucherInput(VoucherTypes.General, D(3, 1), D(3, 1), "Góp vốn", [new(fx.A("CASH"), Side.Debit, 10_000_000), new(fx.A("EQUITY"), Side.Credit, 10_000_000)]));
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 10));
        await PostVoucherAsync(fx, v.Id);
        Assert.Equal(0m, await fx.Bal("AP"));
        Assert.Equal(-50_000m, await fx.Bal("FININC"));
    }

    [Fact]
    public async Task ServiceItem_OrderAcceptanceInvoice_SalesAndPurchase()
    {
        using var fx = await SetupAsync();
        var orders = Orders(fx);
        var acc = new ServiceAcceptanceService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        fx.H.Acc["TRANSPORT"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "TRANSPORT", "Chi phí vận chuyển thuê (thử)")).Id;
        var haul = await fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "VANCHUYEN", "Vận chuyển thuê", "chuyến", "SALESREV", "TRANSPORT");
        Assert.True(haul.IsService); Assert.Null(haul.InventoryAccountId);
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "VANCHUYEN", "x", "x", null, null)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "S2", "x", "x", "AR", null)));          // TK công nợ không dùng làm doanh thu

        // Dịch vụ không đi qua kho.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 1), D(3, 1), "Nhập dịch vụ",
            [new StockLineInput(haul.Id, 1, 100, fx.A("GRNI"))], PartnerId: fx.H.Partner["NCC01"]))));

        // Bán dịch vụ: đơn 10 chuyến → nghiệm thu → hóa đơn trong phạm vi nghiệm thu.
        var order = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Sales, fx.H.Partner["KH01"], D(3, 2), "Vận chuyển thuê tháng 3", [new OrderLineInput(haul.Id, 10, 2_000_000, VatRate: 8)]));
        await orders.ConfirmAsync(fx.H.Approver, order.Id);
        var line = new InvoiceLineInput(2_000_000, Quantity: 4, VatRate: 8, OrderId: order.Id, OrderLineNo: 1);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "S1", D(3, 20), D(3, 20), "Chưa nghiệm thu", [line]))));
        Assert.Equal(ErrorCodes.OverDelivered, await CodeOf(() => acc.CreateAsync(fx.H.Preparer, new(order.Id, D(3, 15), [new(1, 11)]))));
        var nt = await acc.CreateAsync(fx.H.Preparer, new(order.Id, D(3, 15), [new(1, 4)]));
        Assert.StartsWith("NTDV", nt.Number);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "S1", D(3, 20), D(3, 20), "Chờ xác nhận", [line]))));
        Assert.Empty(await acc.InvoiceCandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, null));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => acc.DecideAsync(fx.H.Preparer with { Permissions = new HashSet<string>(fx.H.Preparer.Permissions) { "voucher.approve" } }, nt.Id, true, null)));
        await acc.DecideAsync(fx.H.Approver, nt.Id, true, null);
        var cand = (await acc.InvoiceCandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, fx.H.Partner["KH01"])).Single();
        Assert.Equal((4m, 0m, 4m), (cand.Accepted, cand.Invoiced, cand.Remaining));
        Assert.Equal(fx.A("SALESREV"), cand.AccountId);

        var rep = new ServiceReportService(fx.S.Db, acc);
        var backlog = await rep.ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 1), D(3, 31));
        Assert.Equal(8_000_000m, backlog.BacklogAmount); Assert.Equal(0m, backlog.Total);          // đã nghiệm thu 4 chuyến × 2.000.000, chưa lập hóa đơn
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "S1", D(3, 20), D(3, 20), "Vận chuyển thuê đợt 1", [line]));
        Assert.Equal(haul.Id, inv.Lines[0].ItemId); Assert.Equal(fx.A("SALESREV"), inv.Lines[0].AccountId);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26T", "S2", D(3, 21), D(3, 21), "Vượt", [line with { Quantity = 1 }]))));
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
        Assert.Equal(-8_000_000m, await fx.Bal("SALESREV"));
        Assert.Equal(8_640_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        var done = await rep.ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 1), D(3, 31));
        Assert.Equal((8_000_000m, 640_000m, 0m), (done.Total, done.TotalVat, done.BacklogAmount));
        Assert.Equal(("VANCHUYEN", 4m), (done.ByService.Single().Key, done.ByService.Single().Quantity));
        Assert.Equal(8_000_000m, (await new SalesReportService(fx.S.Db, fx.S.Settlement).AnalyseAsync(fx.H.Auditor, D(3, 1), D(3, 31))).ByType!.Single(t => t.Key == "SERVICE").Revenue);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => acc.CancelAsync(fx.H.Preparer, nt.Id, "Hủy nhầm")));   // đã lập hóa đơn hết phần nghiệm thu
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, D(3, 22), D(3, 22), "Xuất dịch vụ",
            [new StockLineInput(haul.Id, 1, CounterAccountId: fx.A("COGS"))], PartnerId: fx.H.Partner["KH01"]))));
        var lbh = new SalesDispatchService(fx.S.Db, fx.S.Stock, fx.Inv, fx.S.Audit, fx.H.Clock);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => lbh.CreateDispatchAsync(fx.H.Preparer, new DispatchInput(order.Id, fx.Wh, D(3, 22), null, null, [new(1, 1)]))));

        // Mua dịch vụ: chi phí theo TK của dịch vụ, người lập biên bản khác người xác nhận.
        var po = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], D(3, 2), "Thuê xe", [new OrderLineInput(haul.Id, 5, 1_000_000, VatRate: 8)]));
        await orders.ConfirmAsync(fx.H.Approver, po.Id);
        var ntm = await acc.CreateAsync(fx.H.Preparer, new(po.Id, D(3, 16), [new(1, 5)]));
        Assert.StartsWith("NTDM", ntm.Number);
        await acc.DecideAsync(fx.H.Approver, ntm.Id, false, "Thiếu chứng từ vận chuyển");
        Assert.Empty(await acc.InvoiceCandidatesAsync(fx.H.Auditor, InvoiceKind.Purchase, null));
        var ntm2 = await acc.CreateAsync(fx.H.Preparer, new(po.Id, D(3, 17), [new(1, 5)]));       // biên bản bị từ chối không chiếm số lượng
        await acc.DecideAsync(fx.H.Approver, ntm2.Id, true, null);
        var pinv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "1C26T", "M1", D(3, 25), D(3, 25), "Thuê xe",
            [new InvoiceLineInput(1_000_000, Quantity: 5, VatRate: 8, OrderId: po.Id, OrderLineNo: 1)]));
        await fx.Inv.PostAsync(fx.H.Approver, pinv.Id);
        Assert.Equal(5_000_000m, await fx.Bal("TRANSPORT"));
        Assert.Equal(-5_400_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));
    }

    [Fact]
    public async Task ServiceContract_AcceptancePlan_ComparesPlanToActual_AndFlagsOverdue()
    {
        using var fx = await SetupAsync();
        var orders = Orders(fx);
        var acc = new ServiceAcceptanceService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        var plans = new ContractServicePlanService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        var contracts = new ContractService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        var svc = await fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "TUVAN", "Tư vấn kế toán", "giờ", "SALESREV", null);
        var c = await contracts.CreateAsync(fx.H.Preparer, new ContractInput(InvoiceKind.Sales, "HĐ tư vấn 2026", ContractCategory.Quantity, fx.H.Partner["KH01"], D(1, 1), D(12, 31),
            [new ContractLineInput(svc.Id, 100, 500_000, TaxRate: 8), new ContractLineInput(fx.Dam, 10, 1_000_000)]));
        await contracts.SubmitAsync(fx.H.Preparer, c.Id); await contracts.DecideAsync(fx.H.Approver, c.Id, true, null);

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => plans.SetPlanAsync(fx.H.Preparer, c.Id, [new(2, "Giao dăm", D(3, 1), 5)])));                 // dòng hàng hóa
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => plans.SetPlanAsync(fx.H.Preparer, c.Id, [new(1, "Đợt 1", D(3, 1), 60), new(1, "Đợt 2", D(4, 1), 50)])));   // vượt cam kết 100
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => plans.SetPlanAsync(fx.H.Preparer, c.Id, [new(9, "x", D(3, 1), 1)])));
        await plans.SetPlanAsync(fx.H.Preparer, c.Id, [new(1, "Đợt 2", D(4, 30), 40), new(1, "Đợt 1", D(3, 31), 30), new(1, "Đợt 3", D(6, 30), 30)]);

        var order = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Sales, fx.H.Partner["KH01"], D(2, 1), "Tư vấn theo HĐ", [new OrderLineInput(svc.Id, 100, 500_000, VatRate: 8)], ContractId: c.Id));
        await orders.ConfirmAsync(fx.H.Approver, order.Id);
        var today = D(4, 15);
        var rows = await plans.PlanAsync(fx.H.Auditor, c.Id, today);
        Assert.Equal(["Đợt 1", "Đợt 2", "Đợt 3"], rows.Select(r => r.Name).ToArray());               // sắp theo ngày dự kiến
        Assert.Equal(["OVERDUE", "PENDING", "PENDING"], rows.Select(r => r.Status).ToArray());       // chưa nghiệm thu gì, đợt 1 đã quá 31/3
        Assert.Equal([30m, 70m, 100m], rows.Select(r => r.CumulativePlanned).ToArray());

        var nt = await acc.CreateAsync(fx.H.Preparer, new(order.Id, D(4, 10), [new(1, 45)]));
        await acc.DecideAsync(fx.H.Approver, nt.Id, true, null);
        rows = await plans.PlanAsync(fx.H.Auditor, c.Id, today);
        Assert.Equal(["DONE", "PARTIAL", "PENDING"], rows.Select(r => r.Status).ToArray());          // lũy kế 45: đạt đợt 1 (30), đang dở đợt 2 (70)
        Assert.Empty(await plans.OverdueAsync(fx.H.Auditor, InvoiceKind.Sales, today));
        var late = await plans.OverdueAsync(fx.H.Auditor, InvoiceKind.Sales, D(5, 10));
        var one = Assert.Single(late);
        Assert.Equal(("Đợt 2", 10, 25m), (one.Name, one.DaysLate, one.Missing));                      // hạn 30/4, trễ 10 ngày, còn thiếu 70 − 45
        Assert.Empty(await plans.OverdueAsync(fx.H.Auditor, InvoiceKind.Purchase, D(5, 10)));
    }

    [Fact]
    public async Task ServiceProgress_Milestones_AndTimesheet_FeedAcceptances()
    {
        using var fx = await SetupAsync();
        var orders = Orders(fx);
        var acc = new ServiceAcceptanceService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        var prog = new ServiceProgressService(fx.S.Db, acc, fx.S.Audit, fx.H.Clock);
        var proj = await fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "DUAN", "Triển khai dự án", "dự án", "SALESREV", null);
        var hrs = await fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "NGAYCONG", "Tư vấn theo ngày công", "ngày", "SALESREV", null);
        var order = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Sales, fx.H.Partner["KH01"], D(2, 1), "Dự án + tư vấn", [new OrderLineInput(proj.Id, 1, 100_000_000), new OrderLineInput(hrs.Id, 20, 3_000_000)]));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => prog.SetMilestonesAsync(fx.H.Preparer, order.Id, 1, [new("Khởi động", 30, D(3, 1))])));     // đơn chưa xác nhận
        await orders.ConfirmAsync(fx.H.Approver, order.Id);

        // Mốc: 30% + 50% + 20%; tổng không vượt 100%.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => prog.SetMilestonesAsync(fx.H.Preparer, order.Id, 1, [new("A", 60, D(3, 1)), new("B", 50, D(4, 1))])));
        await prog.SetMilestonesAsync(fx.H.Preparer, order.Id, 1, [new("Nghiệm thu", 20, D(6, 1)), new("Khởi động", 30, D(3, 1)), new("Triển khai", 50, D(4, 1))]);
        var ms = await prog.MilestonesAsync(fx.H.Auditor, order.Id, D(3, 15));
        Assert.Equal(["Khởi động", "Triển khai", "Nghiệm thu"], ms.Select(m => m.Name).ToArray());
        Assert.Equal(["OVERDUE", "PENDING", "PENDING"], ms.Select(m => m.Status).ToArray());
        Assert.Equal(0.3m, ms[0].Quantity);
        var a1 = await prog.CompleteMilestoneAsync(fx.H.Preparer, ms[0].Id, D(3, 10));
        var (hh, ss) = (fx.H, fx.S);
        var home = new Hak.Application.Cycles.HomeService(ss.Db, ss.PaymentAcceptances, new Hak.Application.Cycles.SalesReportService(ss.Db, ss.Settlement),
            new Hak.Application.Cycles.PayrollPaymentService(ss.Db, ss.Settlement, ss.PaymentOrders, ss.Audit), new Hak.Application.Cycles.ReorderService(ss.Db), new Hak.Application.Loans.LoanScheduleService(ss.Db, ss.Audit, hh.Clock), hh.Clock,
            new ContractServicePlanService(ss.Db, ss.Audit, hh.Clock));
        Assert.Contains("1 biên bản nghiệm thu dịch vụ chờ xác nhận", (await home.WorkQueueAsync(hh.Auditor)).Single(w => w.Screen == "services").Hint);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => prog.CompleteMilestoneAsync(fx.H.Preparer, ms[0].Id, D(3, 11))));   // đã hoàn thành
        Assert.Equal("WAITING", (await prog.MilestonesAsync(fx.H.Auditor, order.Id, D(3, 15)))[0].Status);
        await acc.DecideAsync(fx.H.Approver, a1.Id, true, null);
        Assert.Equal("DONE", (await prog.MilestonesAsync(fx.H.Auditor, order.Id, D(3, 15)))[0].Status);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => prog.SetMilestonesAsync(fx.H.Preparer, order.Id, 1, [new("Mới", 100, D(7, 1))])));   // đã có mốc hoàn thành
        // Mốc 2: biên bản bị từ chối thì mốc mở lại.
        var a2 = await prog.CompleteMilestoneAsync(fx.H.Preparer, ms[1].Id, D(4, 2));
        await acc.DecideAsync(fx.H.Approver, a2.Id, false, "Chưa đủ hồ sơ");
        Assert.Equal("OVERDUE", (await prog.MilestonesAsync(fx.H.Auditor, order.Id, D(4, 20)))[1].Status);
        await prog.CompleteMilestoneAsync(fx.H.Preparer, ms[1].Id, D(4, 25));

        // Chấm giờ trên dòng 2 (tư vấn theo ngày công): 8 giờ = 1 ngày; giờ đã chốt bị khóa, biên bản bị hủy thì mở lại.
        Assert.Equal(ErrorCodes.AmountNotPositive, await CodeOf(() => prog.AddTimeAsync(fx.H.Preparer, order.Id, 2, D(3, 3), "An", 0, null)));
        Assert.Equal(ErrorCodes.AmountNotPositive, await CodeOf(() => prog.AddTimeAsync(fx.H.Preparer, order.Id, 2, D(3, 3), "An", 25, null)));
        var e1 = await prog.AddTimeAsync(fx.H.Preparer, order.Id, 2, D(3, 3), "An", 8, "Khảo sát");
        await prog.AddTimeAsync(fx.H.Preparer, order.Id, 2, D(3, 4), "Bình", 6, null);
        await prog.AddTimeAsync(fx.H.Preparer, order.Id, 2, D(3, 20), "An", 10, "Sau ngày chốt");
        var nt = await prog.CloseTimesheetAsync(fx.H.Preparer, order.Id, 2, D(3, 10), 8, D(3, 11));
        Assert.Equal(1.75m, nt.Lines.Single().Quantity);                                                       // (8 + 6) giờ ÷ 8
        Assert.Equal(2, (await prog.TimeEntriesAsync(fx.H.Auditor, order.Id, false)).Count(t => t.Locked));
        Assert.Equal(1, (await prog.TimeEntriesAsync(fx.H.Auditor, order.Id, true)).Count);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => prog.DeleteTimeAsync(fx.H.Preparer, e1.Id)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => prog.CloseTimesheetAsync(fx.H.Preparer, order.Id, 2, D(3, 10), 8, D(3, 11))));   // không còn giờ chưa chốt đến 10/3
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => prog.AddTimeAsync(fx.H.Preparer, order.Id, 1, D(3, 3), "", 4, null)));   // thiếu người thực hiện
        await acc.CancelAsync(fx.H.Preparer, nt.Id, "Chốt nhầm kỳ");
        Assert.Equal(3, (await prog.TimeEntriesAsync(fx.H.Auditor, order.Id, true)).Count);                    // giờ được mở lại
        await prog.DeleteTimeAsync(fx.H.Preparer, e1.Id);
        var nt2 = await prog.CloseTimesheetAsync(fx.H.Preparer, order.Id, 2, D(3, 31), 8, D(3, 31));
        Assert.Equal(2m, nt2.Lines.Single().Quantity);                                                         // (6 + 10) giờ ÷ 8
        await acc.DecideAsync(fx.H.Approver, nt2.Id, true, null);
        var cand = (await acc.InvoiceCandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, null)).Where(c => c.ItemCode == "NGAYCONG").Single();
        Assert.Equal(2m, cand.Remaining);                                                                      // hóa đơn lập được theo số nghiệm thu đã xác nhận
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => prog.CloseTimesheetAsync(fx.H.Preparer, order.Id, 2, D(3, 31), 0, D(3, 31))));
    }

    [Fact]
    public async Task SpecialJournals_DuplicateEntries_CountedOnceInMainBook()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        h.Acc["ADV"] = (await s.Setup.CreateAccountAsync(h.Admin, "ADV", "Tạm ứng (thử)")).Id;
        await s.Rules.SetRoleMappingAsync(h.Admin, "ADVANCE", "ADV");
        async Task Post(string desc, params VoucherLineInput[] lines) { var d = D(3, 10); await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, d, d, desc, lines)); }
        // Rút tiền gửi nhập quỹ: vừa thu tiền vừa chi tiền → bút toán trùng.
        await Post("Rút tiền gửi nhập quỹ", new(fx.A("CASH"), Side.Debit, 10_000_000), new(fx.A("BANK"), Side.Credit, 10_000_000));
        // Hoàn ứng mua vật tư (INV là TK kho của vật tư DAM) kèm nộp lại tiền thừa → vừa mua hàng vừa thu tiền.
        await Post("Hoàn ứng mua vật tư", new(fx.A("INV"), Side.Debit, 8_000_000), new(fx.A("CASH"), Side.Debit, 2_000_000), new(fx.A("ADV"), Side.Credit, 10_000_000));
        await Post("Bán hàng", new(fx.A("AR"), Side.Debit, 5_000_000, h.Partner["KH01"]), new(fx.A("SALESREV"), Side.Credit, 5_000_000));
        await Post("Thu tiền khách", new(fx.A("CASH"), Side.Debit, 3_000_000), new(fx.A("AR"), Side.Credit, 3_000_000, h.Partner["KH01"]));
        await Post("Trích chi phí khác", new(fx.A("EXP"), Side.Debit, 1_000_000), new(fx.A("AP"), Side.Credit, 1_000_000, h.Partner["NCC01"]));

        var r = await new SpecialJournalService(s.Db).ReportAsync(h.Auditor, D(3, 1), D(3, 31));
        var b = r.Books.ToDictionary(x => x.Journal);
        // Mỗi bút toán chỉ cộng một lần: rút tiền ở thu tiền, hoàn ứng ở mua hàng.
        Assert.Equal((5_000_000m, 1, 0), (b[Hak.Domain.Ledger.SpecialJournal.Sales].Total, b[Hak.Domain.Ledger.SpecialJournal.Sales].Count, b[Hak.Domain.Ledger.SpecialJournal.Sales].DuplicateCount));
        Assert.Equal((10_000_000m, 1, 0), (b[Hak.Domain.Ledger.SpecialJournal.Purchases].Total, b[Hak.Domain.Ledger.SpecialJournal.Purchases].Count, b[Hak.Domain.Ledger.SpecialJournal.Purchases].DuplicateCount));
        Assert.Equal((13_000_000m, 2, 1), (b[Hak.Domain.Ledger.SpecialJournal.CashReceipts].Total, b[Hak.Domain.Ledger.SpecialJournal.CashReceipts].Count, b[Hak.Domain.Ledger.SpecialJournal.CashReceipts].DuplicateCount));   // rút tiền 10tr + thu khách 3tr; hoàn ứng hiện kèm dấu trùng
        Assert.Equal((0m, 0, 1), (b[Hak.Domain.Ledger.SpecialJournal.CashPayments].Total, b[Hak.Domain.Ledger.SpecialJournal.CashPayments].Count, b[Hak.Domain.Ledger.SpecialJournal.CashPayments].DuplicateCount));   // rút tiền hiện ở chi tiền nhưng trùng
        Assert.Equal((1_000_000m, 1), (b[Hak.Domain.Ledger.SpecialJournal.General].Total, b[Hak.Domain.Ledger.SpecialJournal.General].Count));
        var dup = b[Hak.Domain.Ledger.SpecialJournal.CashPayments].Entries.Single();
        Assert.True(dup.Duplicate); Assert.Equal(Hak.Domain.Ledger.SpecialJournal.CashReceipts, dup.MainJournal);
        // Đối chiếu: tổng Nhật ký = tổng các sổ sau khi loại bút toán trùng.
        Assert.Equal(29_000_000m, r.JournalTotal);
        Assert.Equal(r.JournalTotal, r.BooksTotal); Assert.True(r.Balanced); Assert.Equal(2, r.DuplicateEntries);
    }
}
