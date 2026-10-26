using Hak.Application.Fx;
using Hak.Domain.Production;
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

public partial class TradeTests
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
    public async Task ReverseOriginal_IsBlockedWhilePostedAdjustmentsExist_AndWritesNothing()
    {
        // ERP-P2.3: không đảo hóa đơn gốc khi còn hóa đơn điều chỉnh (giảm giá / trả hàng) đã ghi sổ — đảo điều chỉnh trước.
        using var fx = await SetupAsync();
        var invoice = await PostedPurchaseOfAdditiveAsync(fx);
        var discount = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invoice.Id, InvoicePurpose.Discount, "1C26TAA", "0000310", D(3, 12), D(3, 12),
            "Giảm giá", [new AdjustmentLineInput(1, Amount: 1_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, discount.Id);
        var inv = await fx.Bal("INV"); var ap = await fx.Bal("AP", fx.H.Partner["NCC01"]);
        var journals = await fx.S.Db.JournalEntries.CountAsync();

        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, invoice.Id, D(3, 14), "Đảo hóa đơn gốc")));
        Assert.Equal((inv, ap, journals), (await fx.Bal("INV"), await fx.Bal("AP", fx.H.Partner["NCC01"]), await fx.S.Db.JournalEntries.CountAsync()));   // không có bút toán hay điều chỉnh giá dở dang
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).Status);

        await fx.Inv.ReverseAsync(fx.H.Approver, discount.Id, D(3, 13), "Hủy giảm giá");           // đảo điều chỉnh trước…
        Assert.Equal(InvoiceStatus.Reversed, (await fx.Inv.ReverseAsync(fx.H.Approver, invoice.Id, D(3, 14), "Đảo hóa đơn gốc")).Status);   // …rồi mới đảo được hóa đơn gốc
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
    }

    [Fact]
    public async Task ReversingAPurchaseInvoice_KeepsLedgerSubledgerAndStockReconciled()
    {
        // ERP-P2.5: đối chiếu sổ cái – công nợ – kho sau đảo hóa đơn (có một phần hàng đã xuất nên giá trị chuyển từ tồn kho sang giá vốn).
        using var fx = await SetupAsync();
        var invoice = await PostedPurchaseOfAdditiveAsync(fx);
        await PostedIssueAsync(fx, 40, D(3, 5), fx.Add);
        async Task AssertReconciledAsync(DateOnly asOf)
        {
            Assert.True((await fx.S.Settlement.ReconcileAsync(fx.H.Auditor, asOf)).IsReconciled, "sổ cái ≠ công nợ");
            Assert.All(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, asOf), r => Assert.Equal(0m, r.Difference));   // sổ cái ≠ kho
        }
        await AssertReconciledAsync(D(3, 31));
        await fx.Inv.ReverseAsync(fx.H.Approver, invoice.Id, D(3, 20), "Nhà cung cấp hủy hóa đơn");
        await AssertReconciledAsync(D(3, 31));
        await AssertReconciledAsync(D(3, 10));   // ngày trước đảo cũng khớp
    }

    [Fact]
    public async Task ReverseInvoice_IsBlockedWhenItsStockDocumentBelongsToACompletedProductionOrder()
    {
        // Chốt 24/10/2026: phiếu kho thuộc lệnh sản xuất đã hoàn thành → chặn đảo hóa đơn.
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 150_000_000, D(3, 1), fx.Dam);
        var issue = await PostedIssueAsync(fx, 10, D(3, 10));
        var sale = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TXK", "0000090", D(3, 12), D(3, 12), "Bán",
            [new InvoiceLineInput(2_000_000, issue.Id, 1, VatRate: 8)], DueDate: D(4, 11)));
        await fx.Inv.PostAsync(fx.H.Approver, sale.Id);

        var cost = new CostObject { Id = Ids.New(), CompanyId = fx.H.CompanyId, Code = "CO1", Name = "Dăm", ItemId = fx.Dam, WipAccountId = fx.A("INV"), CreatedAt = fx.H.Clock.UtcNow, CreatedBy = fx.H.AdminId };
        var order = new ProductionOrder { Id = Ids.New(), CompanyId = fx.H.CompanyId, Number = "LSX2026-000001", ItemId = fx.Dam, CostObjectId = cost.Id, WarehouseId = fx.Wh, PlannedQuantity = 10,
            PlannedStart = D(3, 2), Status = ProductionOrderStatus.InProgress, CreatedAt = fx.H.Clock.UtcNow, CreatedBy = fx.H.AdminId };
        fx.S.Db.CostObjects.Add(cost); fx.S.Db.ProductionOrders.Add(order);
        fx.S.Db.ProductionOrderIssues.Add(new ProductionOrderIssue { Id = Ids.New(), CompanyId = fx.H.CompanyId, OrderId = order.Id, StockDocumentId = issue.Id });
        await fx.S.Db.SaveChangesAsync();

        // Lệnh còn đang chạy: đảo được kiểm ở bước khác; đã hoàn thành: chặn và không để lại gì dở dang.
        order.Status = ProductionOrderStatus.Completed; order.CompletedDate = D(3, 15);
        await fx.S.Db.SaveChangesAsync();
        var journals = await fx.S.Db.JournalEntries.CountAsync();
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, sale.Id, D(3, 20), "Đảo")));
        Assert.Equal(journals, await fx.S.Db.JournalEntries.CountAsync());
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == sale.Id)).Status);
        // BIZ-06: đảo trực tiếp phiếu xuất của lệnh đã hoàn thành cũng bị chặn.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.S.Stock.ReverseDocumentAsync(fx.H.Approver, issue.Id, D(3, 20), "Đảo phiếu xuất")));
    }

    [Fact]
    public async Task ReversePurchaseInvoice_IsBlockedWhenTheItemWasConsumedByACompletedProductionOrder()
    {
        // Hóa đơn mua: đảo làm lệch giá kho đã chảy vào giá thành lệnh sản xuất đã hoàn thành → chặn (quy tắc thận trọng).
        using var fx = await SetupAsync();
        var invoice = await PostedPurchaseOfAdditiveAsync(fx);
        var issue = await PostedIssueAsync(fx, 40, D(3, 5), fx.Add);
        var cost = new CostObject { Id = Ids.New(), CompanyId = fx.H.CompanyId, Code = "CO2", Name = "Sản xuất", ItemId = fx.Dam, WipAccountId = fx.A("INV"), CreatedAt = fx.H.Clock.UtcNow, CreatedBy = fx.H.AdminId };
        var order = new ProductionOrder { Id = Ids.New(), CompanyId = fx.H.CompanyId, Number = "LSX2026-000002", ItemId = fx.Dam, CostObjectId = cost.Id, WarehouseId = fx.Wh, PlannedQuantity = 10,
            PlannedStart = D(3, 2), Status = ProductionOrderStatus.InProgress, CreatedAt = fx.H.Clock.UtcNow, CreatedBy = fx.H.AdminId };
        fx.S.Db.CostObjects.Add(cost); fx.S.Db.ProductionOrders.Add(order);
        fx.S.Db.ProductionOrderIssues.Add(new ProductionOrderIssue { Id = Ids.New(), CompanyId = fx.H.CompanyId, OrderId = order.Id, StockDocumentId = issue.Id });
        await fx.S.Db.SaveChangesAsync();
        // Lệnh còn đang chạy: chưa chốt giá thành → vẫn đảo được kiểm ở các bước khác (ở đây chưa chặn bởi quy tắc này).
        order.Status = ProductionOrderStatus.Completed; order.CompletedDate = D(3, 15);
        await fx.S.Db.SaveChangesAsync();
        var journals = await fx.S.Db.JournalEntries.CountAsync();
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, invoice.Id, D(3, 20), "Đảo hóa đơn mua")));
        Assert.Equal(journals, await fx.S.Db.JournalEntries.CountAsync());
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
    }

    [Fact]
    public async Task PostAndReverse_WithStaleRowVersion_AreRejected_ThenSucceedWithTheViewedVersion()
    {
        // Phạm vi bắt buộc version mở rộng: ghi sổ phiếu kho, ghi sổ / đảo hóa đơn đúng bản đã xem.
        using var fx = await SetupAsync();
        var d = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 2), D(3, 2), "Nhập",
            [new StockLineInput(fx.Add, 100, 5_000_000, fx.A("GRNI"))], PartnerId: fx.H.Partner["NCC01"]));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => fx.S.Stock.PostDocumentAsync(fx.H.Approver, d.Id, d.RowVersion - 1)));
        Assert.Equal(StockDocumentStatus.Draft, (await fx.S.Db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == d.Id)).Status);
        await fx.S.Stock.PostDocumentAsync(fx.H.Approver, d.Id, d.RowVersion);

        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "0000555", new InvoiceLineInput(50_000, d.Id, 1, VatRate: 10)));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, inv.Id, false, inv.RowVersion + 1)));
        var posted = await fx.Inv.PostAsync(fx.H.Approver, inv.Id, false, inv.RowVersion);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 20), "Đảo", posted.RowVersion - 1)));
        Assert.Equal(InvoiceStatus.Reversed, (await fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 20), "Đảo", posted.RowVersion)).Status);
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
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
    public async Task Invoice_FromStandaloneIssue_MustBeForTheSamePartner()
    {
        // ERP-P2.2: phiếu xuất KHÔNG có đơn hàng vẫn phải cùng đối tượng với hóa đơn.
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 150_000_000, D(3, 1), fx.Dam);
        var d = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, D(3, 10), D(3, 10), "Xuất bán KH01",
            [new StockLineInput(fx.Dam, 10, CounterAccountId: fx.A("COGS"))], PartnerId: fx.H.Partner["KH01"]));
        var issue = await fx.S.Stock.PostDocumentAsync(fx.H.Approver, d.Id);
        InvoiceInput For(string partner, string no = "0000077") => new(InvoiceKind.Sales, fx.H.Partner[partner], "1C26TXK", no, D(3, 12), D(3, 12), "Bán hàng",
            [new InvoiceLineInput(2_000_000, issue.Id, 1, VatRate: 8)], DueDate: D(4, 11));
        Assert.Equal(ErrorCodes.PartnerMismatch, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, For("KH02"))));
        Assert.Equal(InvoiceStatus.Draft, (await fx.Inv.CreateAsync(fx.H.Preparer, For("KH01"))).Status);

        var d2 = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, D(3, 11), D(3, 11), "Xuất bán KH01 (lần 2)",
            [new StockLineInput(fx.Dam, 5, CounterAccountId: fx.A("COGS"))], PartnerId: fx.H.Partner["KH01"]));
        issue = await fx.S.Stock.PostDocumentAsync(fx.H.Approver, d2.Id);   // phiếu chưa lập hóa đơn nào, cho các lần thử ngoại lệ
        // Ngoại lệ có quyền + lý do + nhật ký: thiếu lý do vẫn bị chặn (kể cả người có quyền); thiếu quyền → Forbidden; đủ cả hai → lập được và có dòng nhật ký.
        Assert.Equal(ErrorCodes.PartnerMismatch, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Approver, For("KH02", "0000079") with { PartnerOverrideReason = "  " })));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, For("KH02", "0000079") with { PartnerOverrideReason = "Khách đổi pháp nhân" })));
        var overridden = await fx.Inv.CreateAsync(fx.H.Approver, For("KH02", "0000078") with { PartnerOverrideReason = "Khách đổi pháp nhân, có công văn 12/2026" });
        Assert.Equal(fx.H.Partner["KH02"], overridden.PartnerId);
        Assert.Contains(await fx.S.Db.AuditLogs.AsNoTracking().ToListAsync(), a => a.Action == "invoice.partner_override" && a.EntityId == overridden.Id.ToString());
        Assert.Equal(InvoiceStatus.Posted, (await fx.Inv.PostAsync(fx.H.Preparer, overridden.Id)).Status);   // ngoại lệ đã có lý do: ghi sổ không bị chặn lại

        // Đối tượng của phiếu nhập nháp bị đổi SAU khi lập hóa đơn thường (không có ngoại lệ): ghi sổ phải phát hiện.
        var receipt = await DraftReceiptAsync(fx, 10, D(3, 15), fx.Add);
        var purchase = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "0000601", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        var tracked = await fx.S.Db.StockDocuments.SingleAsync(x => x.Id == receipt.Id);
        tracked.PartnerId = fx.H.Partner["KH01"];
        await fx.S.Db.SaveChangesAsync();
        Assert.Equal(ErrorCodes.PartnerMismatch, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, purchase.Id)));
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
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
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
    public async Task ReversingAPurchaseInvoice_AfterTheGoodsWereIssued_RepricesTheIssuedPartToCogs_AndKeepsTheStockLedgerUsable()
    {
        using var fx = await SetupAsync();
        var r = await PostedReceiptAsync(fx, 10, 1_000_000, D(3, 1), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "910", new InvoiceLineInput(110_000, r.Id, 1)));   // 1,100,000: +100,000 so với phiếu nhập
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id, acknowledgePriceDifferences: true);                                // toàn bộ còn trong kho → vào tồn kho
        var issue = await PostedIssueAsync(fx, 10, D(3, 5), fx.Add);
        Assert.Equal(1_100_000m, await IssueValue(fx, issue));                                                           // giá đã gồm 100,000 chênh lệch
        Assert.Equal((0m, 1_100_000m), (await fx.Bal("INV"), await fx.Bal("COGS")));

        // Đảo hóa đơn khi hàng đã xuất hết: tính lại đơn giá, 100,000 trả lại giá vốn (không âm tồn kho, không làm hỏng sổ kho).
        await fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 10), "Nhà cung cấp lập sai giá");
        Assert.Equal(0m, await fx.Bal("INV"));
        Assert.Equal(1_000_000m, await fx.Bal("COGS"));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);

        await PostedReceiptAsync(fx, 5, 600_000, D(3, 12), fx.Add);
        Assert.Equal(600_000m, await IssueValue(fx, await PostedIssueAsync(fx, 5, D(3, 13), fx.Add)));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
    }

    [Fact]
    public async Task ReversingAPurchaseInvoice_WithPartOfTheGoodsStillInStock_SplitsTheRepricingByQuantity()
    {
        using var fx = await SetupAsync();
        var r = await PostedReceiptAsync(fx, 10, 1_000_000, D(3, 1), fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "911", new InvoiceLineInput(110_000, r.Id, 1)));
        await fx.Inv.PostAsync(fx.H.Approver, inv.Id, acknowledgePriceDifferences: true);
        await PostedIssueAsync(fx, 4, D(3, 5), fx.Add);                                                                  // 4/10 đã xuất: 440,000 vào giá vốn
        Assert.Equal((660_000m, 440_000m), (await fx.Bal("INV"), await fx.Bal("COGS")));

        await fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 10), "Nhà cung cấp lập sai giá");
        // Chênh lệch 100,000: 60,000 thuộc 6 kg còn trong kho (gỡ khỏi tồn kho), 40,000 thuộc 4 kg đã xuất (giảm giá vốn).
        Assert.Equal((600_000m, 400_000m), (await fx.Bal("INV"), await fx.Bal("COGS")));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        Assert.Equal(600_000m, await IssueValue(fx, await PostedIssueAsync(fx, 6, D(3, 12), fx.Add)));
        Assert.Equal(0m, await fx.Bal("INV"));
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
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
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
    public async Task HomeTasks_ListSpecificDocuments_WithActionOwnerAndBlockedReason()
    {
        // Worklist dẫn thẳng tới chứng từ: mỗi việc mang loại + id chứng từ, hành động kế tiếp, người lập và lý do bị chặn.
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        var home = new Hak.Application.Cycles.HomeService(s.Db, s.PaymentAcceptances, new Hak.Application.Cycles.SalesReportService(s.Db, s.Settlement),
            new Hak.Application.Cycles.PayrollPaymentService(s.Db, s.Settlement, s.PaymentOrders, s.Audit), new Hak.Application.Cycles.ReorderService(s.Db), new Hak.Application.Loans.LoanScheduleService(s.Db, s.Audit, h.Clock), h.Clock, new ContractServicePlanService(s.Db, s.Audit, h.Clock));
        Assert.Empty(await home.TasksAsync(h.Auditor));
        var receipt = await DraftReceiptAsync(fx, 10, D(3, 2));                              // phiếu kho nháp do h.Preparer lập
        var order = await Orders(fx).CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 2), "Đơn bán", [new OrderLineInput(fx.Dam, 1, 1000)]));

        var mine = await home.TasksAsync(h.Preparer);                                         // người lập: ghi sổ phiếu của chính mình bị chặn
        var stockTask = Assert.Single(mine, t => t.EntityType == "stockDocument");
        Assert.Equal((receipt.Id, receipt.Number, "stock", "Ghi sổ phiếu kho"), (stockTask.EntityId, stockTask.Number, stockTask.Screen, stockTask.Action));
        Assert.Contains("cần người khác", stockTask.BlockedReason);
        // Người phụ trách = người có quyền làm bước kế tiếp; người lập (không được tự ghi sổ) không nằm trong danh sách.
        Assert.Contains("Kế toán trưởng", stockTask.Assignees!);
        Assert.DoesNotContain("Kế toán tổng hợp", stockTask.Assignees!);
        var orderTask = Assert.Single(mine, t => t.EntityType == "order");
        Assert.Equal((order.Id, "Sales", "orders"), (orderTask.EntityId, orderTask.Kind, orderTask.Screen));
        Assert.Null(orderTask.BlockedReason);                                                 // xác nhận đơn không bị tách người
        Assert.Equal(stockTask.OwnerName, orderTask.OwnerName);
        Assert.True(mine.ToList().FindIndex(t => t.BlockedReason is null) < mine.ToList().FindIndex(t => t.BlockedReason is not null));   // việc làm được xếp trước

        var approver = await home.TasksAsync(h.Approver);                                     // người khác: ghi sổ được
        Assert.Null(Assert.Single(approver, t => t.EntityType == "stockDocument").BlockedReason);
        Assert.Empty(await home.TasksAsync(h.Admin));                                         // không quyền xem phân hệ nào → không có việc
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
        // Đơn mua và đơn bán là hai chu trình: đếm riêng từng chiều, không cộng lẫn.
        Assert.Equal("Sales", Assert.Single(await home.WorkQueueAsync(h.Auditor), w => w.Screen == "orders").Kind);
        await Orders(fx).CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Purchase, h.Partner["NCC01"], D(3, 2), "Đơn mua", [new OrderLineInput(fx.Add, 5, 1000)]));
        var split = (await home.WorkQueueAsync(h.Auditor)).Where(w => w.Screen == "orders").ToDictionary(w => w.Kind!);
        Assert.Equal((1, 1), (split["Sales"].Count, split["Purchase"].Count));
        Assert.Contains("đơn bán hàng", split["Sales"].Hint);
        Assert.Contains("đơn mua hàng", split["Purchase"].Hint);
        // A settings-only grant (ADMIN: no voucher.view / report.view) cannot open these lists, so it sees no counts either.
        Assert.Empty(await home.WorkQueueAsync(h.Admin));
        Assert.Equal(Hak.Application.Cycles.HomeService.ViewPermission.Keys.OrderBy(k => k), new[] { "assets", "bank", "cash", "loans", "orders", "payroll", "procurement", "production", "revenue", "services", "stock", "trade", "vouchers" });
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
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
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
        // Approving a variance is its own permission (payment.variance), not part of accepting payment; it needs a reason and is bound to the data it was approved on.
        var variance = new Hak.Application.Cycles.PurchaseVarianceService(s.Db, s.Audit, h.Clock);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, "Thỏa thuận", acknowledgeMismatch: true)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => variance.ApproveAsync(h.Preparer, inv.Id, "Thỏa thuận")));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => variance.ApproveAsync(h.Approver, inv.Id, "  ")));
        var approval = await variance.ApproveAsync(h.Approver, inv.Id, "Đã thỏa thuận với NCC trả 10 kg ẩm bằng giảm giá kỳ sau");
        Assert.True(approval.StillValid);
        var acc = await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, "Chấp nhận theo chốt duyệt sai lệch");
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
    public async Task PaymentDiscount_TermsChangedOnThePartnerAfterPosting_DoNotChangeThePostedInvoice()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH01"], null, null, null, null, 30, 2m, 10);   // 2/10 net 30
        var (inv, _) = await PostedBdmtSaleAsync(fx);
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH01"], null, null, null, null, 30, 3m, 10);   // danh mục đổi sang 3/10 sau khi hóa đơn đã ghi sổ
        var c = Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 20)));
        Assert.Equal((2m, 10, 2_664_000m), (c.Percent, c.Days, c.Discount));                                                  // hóa đơn cũ vẫn 2%
        Assert.Equal(2m, (await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id)).PaymentDiscountPercent);
    }

    private static async Task MapPaymentDiscountExpenseAsync(Fx fx)
    {
        fx.H.Acc["FINEXP"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FINEXP", "Chi phí tài chính (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PAYMENT_DISCOUNT_EXPENSE", "FINEXP");
    }

    [Fact]
    public async Task PaymentDiscount_MultiTier_GraceDays_AndVatInclusiveBase()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        await MapPaymentDiscountExpenseAsync(fx);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        // 3/5 và 2/10, thêm 1 ngày dung sai: hạn bậc 3% = 9/3, hạn bậc 2% = 14/3.
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH01"], null, null, null, null, 30, 2m, 10, "tier=3:5;grace=1");
        var inv = await PostedSaleAsync(fx, "0000701", 10, D(3, 3), price: 100_000);                         // 1.000.000 + VAT 100.000
        Assert.Equal("tier=3:5;grace=1", inv.PaymentDiscountOptions);

        var c8 = Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 8)));
        Assert.Equal((3m, 5, D(3, 9), 30_000m, false), (c8.Percent, c8.Days, c8.Deadline, c8.Discount, c8.Expired));
        var c9 = Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 9)));
        Assert.Equal((3m, 30_000m), (c9.Percent, c9.Discount));                                                 // ngày cuối của dung sai vẫn bậc 3%
        var c10 = Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 10)));
        Assert.Equal((2m, 10, D(3, 14), 20_000m), (c10.Percent, c10.Days, c10.Deadline, c10.Discount));          // rơi xuống bậc 2%
        Assert.True(Assert.Single(await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 15))).Expired);

        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 9));
        await PostVoucherAsync(fx, v.Id);
        Assert.Equal(30_000m, await fx.Bal("FINEXP"));
        Assert.Equal(0m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        await LedgerInvariants.AssertAsync(fx.S.Db);

        // Cơ sở gồm thuế: 2% × 1.100.000 = 22.000, thuế GTGT trên hóa đơn vẫn 100.000.
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH02"], null, null, null, null, 30, 2m, 10, "base=incl");
        var inv2 = await PostedSaleAsync(fx, "0000702", 10, D(3, 3), price: 100_000, partner: fx.H.Partner["KH02"]);
        var c = Assert.Single((await pd.CandidatesAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 5))).Where(x => x.PartnerCode == "KH02"));
        Assert.Equal(22_000m, c.Discount);
        Assert.Equal(100_000m, inv2.Lines.Sum(l => l.VatAmount));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH02"], null, null, null, null, 30, 2m, 10, "tier=2:15")));   // bậc xa hơn phải thấp hơn
    }

    [Fact]
    public async Task PaymentDiscount_OneVoucherForSeveralInvoices_DiscountPerInvoice_LateOrTermlessOnlySettle()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        await MapPaymentDiscountExpenseAsync(fx);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var a = await PostedSaleAsync(fx, "0000711", 10, D(3, 3), price: 100_000);                               // không điều khoản: 1.100.000
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH01"], null, null, null, null, 30, 2m, 10);
        var c = await PostedSaleAsync(fx, "0000713", 10, D(3, 3), price: 100_000);                               // 2/10, hạn 13/3 → thanh toán 20/3 là quá hạn
        var b = await PostedSaleAsync(fx, "0000712", 10, D(3, 12), price: 100_000);                              // 2/10, hạn 22/3 → CK 20.000
        var ids = new List<Guid> { a.Id, b.Id, c.Id };
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => pd.CreateMultiVoucherAsync(fx.H.Preparer, InvoiceKind.Sales, fx.H.Partner["KH01"], [a.Id], fx.A("CASH"), D(3, 20))));   // cần ≥ 2 hóa đơn
        var v = await pd.CreateMultiVoucherAsync(fx.H.Preparer, InvoiceKind.Sales, fx.H.Partner["KH01"], ids, fx.A("CASH"), D(3, 20));
        await PostVoucherAsync(fx, v.Id);
        Assert.Equal(20_000m, await fx.Bal("FINEXP"));                                                          // chỉ hóa đơn b
        Assert.Equal(3_300_000m - 20_000m, await fx.Bal("CASH"));                                               // thu đủ ba hóa đơn trừ chiết khấu
        Assert.Equal(0m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        await LedgerInvariants.AssertAsync(fx.S.Db);

        // Chứng từ gộp bị sửa số chiết khấu ngoài dịch vụ → không ghi sổ được.
        var d = await PostedSaleAsync(fx, "0000721", 10, D(3, 21), price: 100_000);
        var e = await PostedSaleAsync(fx, "0000722", 10, D(3, 21), price: 100_000);
        var v2 = await pd.CreateMultiVoucherAsync(fx.H.Preparer, InvoiceKind.Sales, fx.H.Partner["KH01"], [d.Id, e.Id], fx.A("CASH"), D(3, 22));
        await fx.S.Db.VoucherLines.Where(l => l.VoucherId == v2.Id && l.AccountId == fx.A("FINEXP")).ExecuteUpdateAsync(x => x.SetProperty(l => l.Amount, 1m));
        fx.S.Db.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<DomainException>(() => PostVoucherAsync(fx, v2.Id));
    }

    [Fact]
    public async Task PaymentDiscount_ForeignCurrencyInvoice_DiscountInUsd_RealisedGainOnlyOnTheCashPart()
    {
        using var fx = await SetupAsync();
        await MapPaymentDiscountExpenseAsync(fx);
        fx.H.Acc["FXGAIN"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FXGAIN", "Lãi tỷ giá đã thực hiện (thử)")).Id;
        fx.H.Acc["FXLOSS"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FXLOSS", "Lỗ tỷ giá đã thực hiện (thử)")).Id;
        fx.H.Acc["BANKUSD"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "BANKUSD", "Tiền gửi USD (thử)", cashKind: CashKind.Bank)).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "FX_REALIZED_GAIN", "FXGAIN");
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "FX_REALIZED_LOSS", "FXLOSS");
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var issue = await PostedIssueAsync(fx, 10, D(3, 3), fx.Add);
        var created = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TXK", "0000801", D(3, 3), D(3, 3), "Bán USD",
            [new InvoiceLineInput(100, issue.Id, 1, VatRate: 0)], Currency: "USD", ExchangeRate: 25_000, PaymentDiscountPercent: 2, PaymentDiscountDays: 10));
        await fx.Inv.PostAsync(fx.H.Approver, created.Id);                                                        // 1.000 USD = 25.000.000
        var item = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(3, 10)));

        // Quá hạn (ngày 14/3 > hạn 13/3): từ chối. Không đủ điều khoản không có ApplyDiscount thì thu bình thường.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.FxPayments.CreateDraftAsync(fx.H.Preparer,
            new FxPaymentInput(D(3, 14), fx.A("BANKUSD"), 25_200, "Thu muộn", [new FxSettleItem(item.JournalLineId, 1_000, ApplyDiscount: true)]))));

        var input = new FxPaymentInput(D(3, 10), fx.A("BANKUSD"), 25_200, "Thu KH01 có chiết khấu", [new FxSettleItem(item.JournalLineId, 1_000, ApplyDiscount: true)]);
        var preview = await fx.S.FxPayments.PreviewAsync(fx.H.Preparer, input);
        Assert.Equal((980m, 20m, 500_000m, 196_000m), (preview.ForeignTotal, preview.DiscountForeign, preview.DiscountBook, preview.Difference));
        var v = await fx.S.FxPayments.CreateDraftAsync(fx.H.Preparer, input);
        await PostVoucherAsync(fx, v.Id);
        Assert.Equal(0m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(500_000m, await fx.Bal("FINEXP"));                                                          // 20 USD × 25.000 theo giá trị ghi sổ của hóa đơn
        Assert.Equal(24_696_000m, await fx.Bal("BANKUSD"));                                                      // 980 USD × 25.200
        Assert.Equal(-196_000m, await fx.Bal("FXGAIN"));                                                         // chênh lệch tỷ giá chỉ trên phần tiền thực thu
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task PaymentDiscountReports_MissedSuspectDaysAndEffect()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        var rep = new PaymentDiscountReportService(fx.S.Db, fx.S.Ledger, fx.S.Audit, fx.H.Clock);
        await MapPaymentDiscountExpenseAsync(fx);
        await PostedReceiptAsync(fx, 200, 10_000_000, D(3, 1), fx.Add);
        InvoiceInput Sale(string no, StockDocument issue) => new(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TBB", no, D(3, 3), D(3, 3), "Bán", [new InvoiceLineInput(100_000, issue.Id, 1, VatRate: 10)],
            DueDate: D(4, 2), PaymentDiscountPercent: 2, PaymentDiscountDays: 10);
        var i1 = await fx.Inv.PostAsync(fx.H.Approver, (await fx.Inv.CreateAsync(fx.H.Preparer, Sale("0000901", await PostedIssueAsync(fx, 10, D(3, 3), fx.Add)))).Id);
        var i2 = await fx.Inv.PostAsync(fx.H.Approver, (await fx.Inv.CreateAsync(fx.H.Preparer, Sale("0000902", await PostedIssueAsync(fx, 10, D(3, 3), fx.Add)))).Id);
        var i3 = await fx.Inv.PostAsync(fx.H.Approver, (await fx.Inv.CreateAsync(fx.H.Preparer, Sale("0000903", await PostedIssueAsync(fx, 10, D(3, 3), fx.Add)))).Id);

        // i1 hưởng chiết khấu đúng hạn (10/3): CK 20.000, tiền 1.080.000, sớm 23 ngày so với hạn 2/4.
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, i1.Id, fx.A("CASH"), D(3, 10));
        await PostVoucherAsync(fx, v.Id);
        // i2: khách chỉ trả 1.078.000 trong khi công nợ 1.100.000 (tự trừ 2%) — còn dư 22.000.
        var open2 = (await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(3, 20))).Single(o => o.VoucherId == i2.VoucherId);
        await fx.H.PostedAsync(fx.S, new VoucherInput(VoucherTypes.General, D(3, 12), D(3, 12), "Thu thiếu", [new(fx.A("CASH"), Side.Debit, 1_078_000), new(fx.A("AR"), Side.Credit, 1_078_000, fx.H.Partner["KH01"], SettleJournalLineId: open2.JournalLineId)]));

        var missed = await rep.MissedAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 20));                          // hạn hưởng 13/3 đã qua
        var m = Assert.Single(missed.Where(x => x.InvoiceId == i3.Id));
        Assert.Equal((20_000m, 1_100_000m, 7), (m.LostDiscount, m.Open, m.DaysOver));
        Assert.DoesNotContain(missed, x => x.InvoiceId == i1.Id);                                                // đã thanh toán, không còn nợ
        Assert.Empty(await rep.MissedAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 12)));                          // chưa qua hạn

        var suspect = Assert.Single(await rep.SuspectDeductionsAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 20)));
        Assert.Equal((i2.Id, 22_000m, 2m), (suspect.InvoiceId, suspect.Open, suspect.OpenPercent));

        var effect = await rep.EffectAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 1), D(3, 31));
        var row = Assert.Single(effect.Rows);
        Assert.Equal((20_000m, 1_080_000m, 23, 29.39m), (row.Discount, row.Cash, row.DaysEarly, row.AnnualRatePercent));
        Assert.Equal(29.39m, effect.WeightedAnnualRatePercent);

        var days = await rep.DaysAsync(fx.H.Auditor, InvoiceKind.Sales, D(3, 1), D(3, 31));
        var march = Assert.Single(days);
        Assert.Equal(3_300_000m, march.Turnover);
        Assert.True(march.Days > 0);
    }

    [Fact]
    public async Task AdjustmentInvoice_AfterCutover_ChangingTaxOfAFiledPeriod_FlagsASupplementalFiling()
    {
        using var fx = await SetupAsync();
        var vat = new Hak.Application.Tax.VatService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var sale = await PostedSaleAsync(fx, "0001001", 10, D(3, 3), price: 100_000);                            // thuế 100.000 ở tháng 3
        var decl = await vat.SaveDeclarationAsync(fx.H.Admin, D(3, 1), D(3, 31), [new Hak.Application.Tax.DeclarationLineInput("CT29", "Thuế GTGT đầu ra (thử)", 100_000)]);
        // Điều chỉnh trước 01/07/2026 hoặc khi kỳ gốc chưa nộp tờ khai: kê khai ở kỳ hiện tại, không cờ.
        var early = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(sale.Id, InvoicePurpose.Discount, "1C26TBB", "0001002", D(6, 10), D(6, 10), "Giảm giá tháng 6", [new AdjustmentLineInput(1, Amount: 100_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, early.Id);
        Assert.DoesNotContain((await vat.ReconcileAsync(fx.H.Auditor, D(6, 1), D(6, 30))).Differences, d => d.Type == Hak.Application.Tax.TaxDiffType.SupplementalFilingNeeded);

        var late = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(sale.Id, InvoicePurpose.Discount, "1C26TBB", "0001003", D(7, 10), D(7, 10), "Sửa sai đơn giá hóa đơn tháng 3", [new AdjustmentLineInput(1, Amount: 100_000)], AdjustmentReason: InvoiceAdjustmentReason.InvoiceError));
        await fx.Inv.PostAsync(fx.H.Approver, late.Id);
        Assert.DoesNotContain((await vat.ReconcileAsync(fx.H.Auditor, D(7, 1), D(7, 31))).Differences, d => d.Type == Hak.Application.Tax.TaxDiffType.SupplementalFilingNeeded);   // kỳ gốc chưa nộp tờ khai
        await vat.MarkFiledAsync(fx.H.Admin, decl.Id, "TK-03/2026", D(4, 20));
        var flag = Assert.Single((await vat.ReconcileAsync(fx.H.Auditor, D(7, 1), D(7, 31))).Differences.Where(d => d.Type == Hak.Application.Tax.TaxDiffType.SupplementalFilingNeeded));
        Assert.Equal(late.Id, flag.InvoiceId);
        Assert.Contains("kỳ", flag.Suggestion);
        Assert.DoesNotContain((await vat.ReconcileAsync(fx.H.Auditor, D(6, 1), D(6, 30))).Differences, d => d.Type == Hak.Application.Tax.TaxDiffType.SupplementalFilingNeeded);   // trước 01/07/2026 theo luật cũ
    }

    [Fact]
    public async Task PaymentDiscount_Standalone_NotTiedToAnInvoice_IsASeparateEntry()
    {
        using var fx = await SetupAsync();
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        var receipt = await DraftReceiptAsync(fx, 100, D(3, 2), fx.Add);
        var created = await fx.Inv.CreateAsync(fx.H.Preparer, Purchase(fx, "M1", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        var inv = await fx.Inv.PostAsync(fx.H.Approver, created.Id);                                                                                // phải trả 5.500.000, hóa đơn không có điều khoản
        fx.H.Acc["FININC"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FININC", "Doanh thu tài chính (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PAYMENT_DISCOUNT_INCOME", "FININC");
        await fx.H.PostedAsync(fx.S, new VoucherInput(VoucherTypes.General, D(3, 1), D(3, 1), "Góp vốn", [new(fx.A("CASH"), Side.Debit, 10_000_000), new(fx.A("EQUITY"), Side.Credit, 10_000_000)]));
        var item = Assert.Single(await pd.OpenItemsAsync(fx.H.Auditor, InvoiceKind.Purchase, fx.H.Partner["NCC01"], D(3, 10)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => pd.CreateStandaloneVoucherAsync(fx.H.Preparer, InvoiceKind.Purchase, fx.H.Partner["NCC01"], item.JournalLineId, fx.A("CASH"), D(3, 10), 0)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => pd.CreateStandaloneVoucherAsync(fx.H.Preparer, InvoiceKind.Purchase, fx.H.Partner["NCC01"], item.JournalLineId, fx.A("CASH"), D(3, 10), item.Open)));
        var v = await pd.CreateStandaloneVoucherAsync(fx.H.Preparer, InvoiceKind.Purchase, fx.H.Partner["NCC01"], item.JournalLineId, fx.A("CASH"), D(3, 10), 100_000);
        await PostVoucherAsync(fx, v.Id);
        Assert.Equal(0m, await fx.Bal("AP"));
        Assert.Equal(-100_000m, await fx.Bal("FININC"));
        var after = await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(x => x.Id == inv.Id);
        Assert.Equal((inv.Status, inv.RowVersion), (after.Status, after.RowVersion));                                                               // hóa đơn không đổi
        var rep = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Purchase, D(3, 1), D(3, 31));
        Assert.Equal(100_000m, rep.Total.PaymentDiscount);
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
        Assert.Contains("1 biên bản nghiệm thu dịch vụ bán chờ xác nhận", (await home.WorkQueueAsync(hh.Auditor)).Single(w => w.Screen == "services" && w.Kind == "Sales").Hint);
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
        // Nộp tiền mặt vào ngân hàng: tiền mặt ưu tiên hơn ngân hàng → chiều Có tiền mặt quyết định, ghi ở chi tiền; hiện trùng ở thu tiền.
        await Post("Nộp tiền mặt vào ngân hàng", new(fx.A("BANK"), Side.Debit, 4_000_000), new(fx.A("CASH"), Side.Credit, 4_000_000));
        await Post("Bán hàng", new(fx.A("AR"), Side.Debit, 5_000_000, h.Partner["KH01"]), new(fx.A("SALESREV"), Side.Credit, 5_000_000));
        await Post("Thu tiền khách", new(fx.A("CASH"), Side.Debit, 3_000_000), new(fx.A("AR"), Side.Credit, 3_000_000, h.Partner["KH01"]));
        await Post("Trích chi phí khác", new(fx.A("EXP"), Side.Debit, 1_000_000), new(fx.A("AP"), Side.Credit, 1_000_000, h.Partner["NCC01"]));

        var r = await new SpecialJournalService(s.Db).ReportAsync(h.Auditor, D(3, 1), D(3, 31));
        var b = r.Books.ToDictionary(x => x.Journal);
        // Mỗi bút toán chỉ cộng một lần, ưu tiên tiền mặt: rút tiền và hoàn ứng (có hoàn tiền thừa) đều cộng ở thu tiền.
        Assert.Equal((5_000_000m, 1, 0), (b[Hak.Domain.Ledger.SpecialJournal.Sales].Total, b[Hak.Domain.Ledger.SpecialJournal.Sales].Count, b[Hak.Domain.Ledger.SpecialJournal.Sales].DuplicateCount));
        Assert.Equal((0m, 0, 1), (b[Hak.Domain.Ledger.SpecialJournal.Purchases].Total, b[Hak.Domain.Ledger.SpecialJournal.Purchases].Count, b[Hak.Domain.Ledger.SpecialJournal.Purchases].DuplicateCount));    // hoàn ứng hiện ở mua hàng nhưng trùng
        Assert.Equal((23_000_000m, 3, 1), (b[Hak.Domain.Ledger.SpecialJournal.CashReceipts].Total, b[Hak.Domain.Ledger.SpecialJournal.CashReceipts].Count, b[Hak.Domain.Ledger.SpecialJournal.CashReceipts].DuplicateCount));   // rút tiền 10tr + hoàn ứng 10tr + thu khách 3tr
        Assert.Equal((4_000_000m, 1, 1), (b[Hak.Domain.Ledger.SpecialJournal.CashPayments].Total, b[Hak.Domain.Ledger.SpecialJournal.CashPayments].Count, b[Hak.Domain.Ledger.SpecialJournal.CashPayments].DuplicateCount));   // nộp tiền mặt vào ngân hàng 4tr cộng ở chi tiền; rút tiền hiện ở chi tiền nhưng trùng
        Assert.Equal((1_000_000m, 1), (b[Hak.Domain.Ledger.SpecialJournal.General].Total, b[Hak.Domain.Ledger.SpecialJournal.General].Count));
        var dup = b[Hak.Domain.Ledger.SpecialJournal.CashPayments].Entries.Single(e => e.Duplicate);
        Assert.True(dup.Duplicate); Assert.Equal(Hak.Domain.Ledger.SpecialJournal.CashReceipts, dup.MainJournal);
        // Đối chiếu: tổng Nhật ký = tổng các sổ sau khi loại bút toán trùng.
        Assert.Equal(33_000_000m, r.JournalTotal);
        Assert.Equal(r.JournalTotal, r.BooksTotal); Assert.True(r.Balanced); Assert.Equal(3, r.DuplicateEntries);   // rút tiền (hiện ở chi tiền), nộp tiền vào ngân hàng (hiện ở thu tiền), hoàn ứng (hiện ở mua hàng)
    }

    // ---- Tổng số lượng trên MỘT hóa đơn có nhiều dòng cùng nguồn (đơn dịch vụ / dòng phiếu kho) ----

    private static async Task<Guid> AcceptedServiceOrderAsync(Fx fx, InvoiceKind kind, decimal ordered, decimal accepted)
    {
        var acc = new ServiceAcceptanceService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        fx.H.Acc["TRANSPORT"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "TRANSPORT", "Chi phí vận chuyển thuê (thử)")).Id;
        var haul = await fx.S.Stock.CreateServiceItemAsync(fx.H.Admin, "VANCHUYEN", "Vận chuyển thuê", "chuyến", "SALESREV", "TRANSPORT");
        var partner = kind == InvoiceKind.Sales ? fx.H.Partner["KH01"] : fx.H.Partner["NCC01"];
        var order = await Orders(fx).CreateAsync(fx.H.Preparer, new OrderInput(kind, partner, D(3, 2), "Dịch vụ vận chuyển", [new OrderLineInput(haul.Id, ordered, 2_000_000, VatRate: 8)]));
        await Orders(fx).ConfirmAsync(fx.H.Approver, order.Id);
        var nt = await acc.CreateAsync(fx.H.Preparer, new(order.Id, D(3, 15), [new(1, accepted)]));
        await acc.DecideAsync(fx.H.Approver, nt.Id, true, null);
        return order.Id;
    }

    private static InvoiceInput ServiceInvoice(Fx fx, InvoiceKind kind, string no, params InvoiceLineInput[] lines)
        => new(kind, kind == InvoiceKind.Sales ? fx.H.Partner["KH01"] : fx.H.Partner["NCC01"], "1C26T", no, D(3, 20), D(3, 20), "Dịch vụ vận chuyển", lines);

    private static async Task<int> JournalCountAsync(Fx fx) => await fx.S.Db.JournalEntries.CountAsync();

    [Fact]
    public async Task ServiceInvoice_TwoLinesOfSameOrderLine_AreSummed_Sales()
    {
        using var fx = await SetupAsync();
        var order = await AcceptedServiceOrderAsync(fx, InvoiceKind.Sales, 10, 10);
        InvoiceLineInput L(decimal q, decimal price = 2_000_000) => new(price, Quantity: q, VatRate: 8, OrderId: order, OrderLineNo: 1);

        // 6 + 6 > 10: bị chặn ngay lúc lập, không để lại hóa đơn nào.
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, ServiceInvoice(fx, InvoiceKind.Sales, "A1", L(6), L(6)))));
        Assert.Equal(0, await fx.S.Db.CommercialInvoices.CountAsync());

        // 6 + 4 = 10 hợp lệ (cho phép tách dòng theo đơn giá khác nhau; cộng theo số lượng).
        var ok = await fx.Inv.CreateAsync(fx.H.Preparer, ServiceInvoice(fx, InvoiceKind.Sales, "A2", L(6), L(4, 2_500_000)));
        // Nháp giữ chỗ: hóa đơn khác chỉ thêm 1 cũng vượt.
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, ServiceInvoice(fx, InvoiceKind.Sales, "A3", L(1)))));
        await fx.Inv.PostAsync(fx.H.Approver, ok.Id);
        Assert.Equal(-22_000_000m, await fx.Bal("SALESREV"));   // 6 × 2.000.000 + 4 × 2.500.000

        // Ghi sổ lặp không phát sinh thêm bút toán.
        var before = await JournalCountAsync(fx);
        Assert.Equal(ErrorCodes.AlreadyPosted, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, ok.Id)));
        Assert.Equal(before, await JournalCountAsync(fx));
    }

    [Fact]
    public async Task ServiceInvoice_TwoLinesOfSameOrderLine_AreSummed_Purchase()
    {
        using var fx = await SetupAsync();
        var order = await AcceptedServiceOrderAsync(fx, InvoiceKind.Purchase, 5, 5);
        InvoiceLineInput L(decimal q) => new(2_000_000, Quantity: q, VatRate: 8, OrderId: order, OrderLineNo: 1);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, ServiceInvoice(fx, InvoiceKind.Purchase, "M1", L(3), L(3)))));
        var ok = await fx.Inv.CreateAsync(fx.H.Preparer, ServiceInvoice(fx, InvoiceKind.Purchase, "M2", L(2), L(3)));
        await fx.Inv.PostAsync(fx.H.Approver, ok.Id);
        Assert.Equal(10_000_000m, await fx.Bal("TRANSPORT"));
    }

    [Fact]
    public async Task ServiceInvoice_DraftEditedPastAcceptance_IsRejectedAtPosting_WithoutPartialState()
    {
        using var fx = await SetupAsync();
        var order = await AcceptedServiceOrderAsync(fx, InvoiceKind.Sales, 10, 10);
        InvoiceLineInput L(decimal q) => new(2_000_000, Quantity: q, VatRate: 8, OrderId: order, OrderLineNo: 1);
        var draft = await fx.Inv.CreateAsync(fx.H.Preparer, ServiceInvoice(fx, InvoiceKind.Sales, "B1", L(6), L(4)));
        // Mô phỏng dữ liệu cũ / sửa trực tiếp: dòng 2 bị nâng lên 6 (tổng 12 > 10) sau khi đã lập.
        var line2 = await fx.S.Db.CommercialInvoiceLines.SingleAsync(x => x.InvoiceId == draft.Id && x.LineNo == 2);
        line2.BasisQuantity = 6;
        await fx.S.Db.SaveChangesAsync();
        fx.S.Db.ChangeTracker.Clear();

        var journalsBefore = await JournalCountAsync(fx);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, draft.Id)));
        Assert.Equal(journalsBefore, await JournalCountAsync(fx));
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == draft.Id)).Status);
        Assert.Equal(0m, await fx.Bal("SALESREV"));
    }

    [Fact]
    public async Task StockInvoice_TwoLinesOfSameStockLine_AreSummed()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 10, 1_000, D(3, 1), fx.Add);
        var issue = await PostedIssueAsync(fx, 10, D(3, 2), fx.Add);
        InvoiceLineInput L(decimal q) => new(500, issue.Id, 1, StockQuantity: q, VatRate: 10);
        InvoiceInput Sale(string no, params InvoiceLineInput[] lines) => new(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", no, D(3, 3), D(3, 3), "Bán phụ gia", lines);

        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, Sale("C1", L(6), L(6)))));
        Assert.Equal(0, await fx.S.Db.CommercialInvoices.CountAsync());
        var ok = await fx.Inv.CreateAsync(fx.H.Preparer, Sale("C2", L(4), L(6)));
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, Sale("C3", L(1)))));
        await fx.Inv.PostAsync(fx.H.Approver, ok.Id);
    }

    // ---- Chiết khấu thanh toán: điều kiện nguồn (hạn) được kiểm tra lại ở mọi bước, không sửa tay chứng từ nháp ----

    private static async Task<(PaymentDiscountService Pd, CommercialInvoice Inv)> DiscountSaleAsync(Fx fx)
    {
        var pd = new PaymentDiscountService(fx.S.Db, fx.S.Engine, fx.S.Audit, fx.H.Clock);
        await fx.S.Setup.UpdatePartnerCommercialAsync(fx.H.Admin, fx.H.Partner["KH01"], null, null, null, null, 30, 2m, 10);   // 2% nếu trả trong 10 ngày kể từ ngày hóa đơn
        var (inv, _) = await PostedBdmtSaleAsync(fx);                                                                              // ngày HĐ 12/3 → hạn 22/3
        fx.H.Acc["FINEXP"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "FINEXP", "Chi phí tài chính (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PAYMENT_DISCOUNT_EXPENSE", "FINEXP");
        return (pd, inv);
    }

    private static VoucherInput SameVoucherOnDate(Voucher v, DateOnly date)
        => new(VoucherTypes.General, date, date, v.Description,
            v.Lines.Select(l => new VoucherLineInput(l.AccountId, l.Side, l.Amount, l.PartnerId, Description: l.Description, SettleJournalLineId: l.SettleJournalLineId)).ToList(),
            SourceSystem: v.SourceSystem, SourceId: v.SourceId);

    [Fact]
    public async Task Audit_PaymentDiscount_PostingAfterDeadline_MustBeRejected()
    {
        using var fx = await SetupAsync();
        var (pd, inv) = await DiscountSaleAsync(fx);
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 20));      // trong hạn

        // 1) Sửa ngày chứng từ nháp ra ngoài hạn: bị từ chối (chứng từ chiết khấu không sửa tay).
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Engine.UpdateDraftAsync(fx.H.Preparer, v.Id, v.RowVersion, SameVoucherOnDate(v, D(3, 25)))));

        // 2) Dù ngày bị đổi ngoài đường dịch vụ (dữ liệu cũ / sửa trực tiếp), gửi duyệt và ghi sổ vẫn kiểm tra lại hạn nên bị chặn, không có journal.
        var journalsBefore = await JournalCountAsync(fx);
        var tracked = await fx.S.Db.Vouchers.SingleAsync(x => x.Id == v.Id);
        tracked.PostingDate = D(3, 25); tracked.VoucherDate = D(3, 25);
        await fx.S.Db.SaveChangesAsync();
        fx.S.Db.ChangeTracker.Clear();
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Engine.SubmitAsync(fx.H.Preparer, v.Id)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => PostVoucherAsync(fx, v.Id)));
        Assert.Equal(journalsBefore, await JournalCountAsync(fx));
        Assert.Equal(VoucherStatus.Draft, (await fx.S.Db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == v.Id)).Status);
        Assert.Equal(143_856_000m, await fx.Bal("AR"));        // công nợ giữ nguyên, chưa thu
    }

    [Fact]
    public async Task PaymentDiscount_LastDayAllowed_DayAfterRejected_CancelThenRecreate()
    {
        using var fx = await SetupAsync();
        var (pd, inv) = await DiscountSaleAsync(fx);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 23))));   // 23/3 > hạn 22/3
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 22));                                                  // đúng ngày cuối: hợp lệ
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 21))));  // một nguồn một chứng từ còn hiệu lực
        await fx.S.Engine.CancelAsync(fx.H.Preparer, v.Id, "Chọn nhầm tài khoản tiền");
        var again = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 21));                                              // hủy rồi tạo lại
        await PostVoucherAsync(fx, again.Id);
        Assert.Equal(0m, await fx.Bal("AR"));
        Assert.Equal(2_664_000m, await fx.Bal("FINEXP"));
    }

    [Fact]
    public async Task PaymentDiscount_AmountsEditedOutsideTheService_AreRejected()
    {
        using var fx = await SetupAsync();
        var (pd, inv) = await DiscountSaleAsync(fx);
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 20));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Engine.UpdateDraftAsync(fx.H.Preparer, v.Id, v.RowVersion,
            SameVoucherOnDate(v, D(3, 20)) with { Lines = v.Lines.Select(l => new VoucherLineInput(l.AccountId, l.Side, l.Side == Side.Debit && l.AccountId == fx.A("FINEXP") ? l.Amount * 2 : l.Amount, l.PartnerId,
                SettleJournalLineId: l.SettleJournalLineId)).ToList() })));

        // Số chiết khấu bị nâng lên ngoài đường dịch vụ (và tiền thực thu giảm tương ứng để chứng từ vẫn cân): kiểm tra lại công thức nên bị chặn.
        var disc = await fx.S.Db.VoucherLines.SingleAsync(l => l.VoucherId == v.Id && l.AccountId == fx.A("FINEXP"));
        var cash = await fx.S.Db.VoucherLines.SingleAsync(l => l.VoucherId == v.Id && l.AccountId == fx.A("CASH"));
        disc.Amount += 1_000_000; cash.Amount -= 1_000_000;
        await fx.S.Db.SaveChangesAsync();
        fx.S.Db.ChangeTracker.Clear();
        var journalsBefore = await JournalCountAsync(fx);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => PostVoucherAsync(fx, v.Id)));
        Assert.Equal(journalsBefore, await JournalCountAsync(fx));
    }

    [Fact]
    public async Task PaymentDiscount_SourceInvoiceReversedAfterDraft_VoucherCannotBePosted()
    {
        using var fx = await SetupAsync();
        var (pd, inv) = await DiscountSaleAsync(fx);
        var v = await pd.CreateVoucherAsync(fx.H.Preparer, inv.Id, fx.A("CASH"), D(3, 20));
        await fx.Inv.ReverseAsync(fx.H.Approver, inv.Id, D(3, 21), "Lập sai hóa đơn");
        var journalsBefore = await JournalCountAsync(fx);
        await Assert.ThrowsAsync<DomainException>(() => PostVoucherAsync(fx, v.Id));
        Assert.Equal(journalsBefore, await JournalCountAsync(fx));
        Assert.Equal(0m, await fx.Bal("CASH"));
    }

    // ------------------------------------------------------------------ giới hạn điều chỉnh / trả hàng (MAIN-01, MAIN-02)

    private static AdjustmentInput DiscountOf(CommercialInvoice inv, string no, params decimal[] amounts)
        => new(inv.Id, InvoicePurpose.Discount, "1C26T", no, D(3, 20), D(3, 20), "Giảm giá", amounts.Select(a => new AdjustmentLineInput(1, Amount: a)).ToList());

    private static Task<int> JournalsAsync(Fx fx) => fx.S.Db.JournalEntries.CountAsync();

    [Fact]
    public async Task Discount_TwoLinesOnSameOriginalLine_AreSummed_AndNothingIsBooked()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);                       // line value 133,200,000
        var journals = await JournalsAsync(fx);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, DiscountOf(inv, "200", 79_920_000, 79_920_000))));
        Assert.Equal(0, await fx.S.Db.CommercialInvoices.CountAsync(i => i.Purpose == InvoicePurpose.Discount));
        Assert.Equal(journals, await JournalsAsync(fx));

        var ok = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, DiscountOf(inv, "201", 53_280_000, 79_920_000));   // 40% + 60% = exactly the line
        await fx.Inv.PostAsync(fx.H.Approver, ok.Id);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, DiscountOf(inv, "202", 1))));
    }

    [Fact]
    public async Task Discount_AcrossInvoices_Accumulates_CancelReleases_AndPostingRechecksTheTotal()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);
        var first = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, DiscountOf(inv, "210", 79_920_000));
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, DiscountOf(inv, "211", 79_920_000))));
        await fx.Inv.CancelAsync(fx.H.Preparer, first.Id, "Lập nhầm");
        var second = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, DiscountOf(inv, "211", 79_920_000));          // cancelled draft no longer counts

        // A draft edited outside the service past the limit is stopped when it is posted, with no half-written state.
        await fx.S.Db.CommercialInvoiceLines.Where(l => l.InvoiceId == second.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.FunctionalAmount, 140_000_000m));
        fx.S.Db.ChangeTracker.Clear();                                       // ExecuteUpdate bypasses tracking; a real request starts with a fresh context
        var journals = await JournalsAsync(fx);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, second.Id)));
        fx.S.Db.ChangeTracker.Clear();
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == second.Id)).Status);
        Assert.Equal(journals, await JournalsAsync(fx));
    }

    [Fact]
    public async Task ReturnReceipts_WithoutCreditNotes_CannotExceedTheQuantitySold()
    {
        using var fx = await SetupAsync();
        var (inv, _) = await PostedBdmtSaleAsync(fx);                       // 60 MT sold; approval policy is off
        var r1 = await fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 40, D(3, 15));
        await fx.S.Stock.PostDocumentAsync(fx.H.Approver, r1.Id);           // posted, still no credit note
        var movements = await fx.S.Db.StockMovements.CountAsync();
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 40, D(3, 16))));
        Assert.Equal(movements, await fx.S.Db.StockMovements.CountAsync());
        await fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 20, D(3, 16));                                   // 40 + 20 = 60 is fine
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 1, D(3, 17))));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
    }

    [Fact]
    public async Task ReturnReceipt_DirectApiDrafts_AreValidatedAtCreation_AndRecheckedAtPosting()
    {
        using var fx = await SetupAsync();
        var (inv, issue) = await PostedBdmtSaleAsync(fx);                   // 60 MT sold
        var line = issue.Lines[0];
        Task<StockDocument> Raw(decimal qty, Guid? partner = null, Guid? invoice = null, Guid? item = null, int lineNo = 1, StockDocumentType type = StockDocumentType.Receipt)
            => fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(type, fx.Wh, D(3, 15), D(3, 15), "Nhận trả",
                [new StockLineInput(item ?? line.ItemId, qty, type == StockDocumentType.Receipt ? 1_000_000 : null, line.CounterAccountId, "Trả", SourceInvoiceId: invoice ?? inv.Id, SourceInvoiceLineNo: lineNo)],
                PartnerId: partner ?? inv.PartnerId));

        // Source must be a real line of a posted sales invoice of the same customer, the same item, and the receipt direction.
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => Raw(1, invoice: Guid.NewGuid())));
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => Raw(1, lineNo: 9)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => Raw(1, partner: fx.H.Partner["NCC01"])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => Raw(1, item: fx.Add)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => Raw(1, type: StockDocumentType.Issue)));

        // Drafts reserve the quantity: 40 + 40 > 60 is refused when the second is created, 40 + 20 is fine.
        var a = await Raw(40);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => Raw(40)));
        var b = await Raw(20);

        // A draft edited outside the service is stopped again when it is posted.
        await fx.S.Db.StockDocumentLines.Where(l => l.DocumentId == b.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Quantity, 40m));
        fx.S.Db.ChangeTracker.Clear();
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.S.Stock.PostDocumentAsync(fx.H.Approver, a.Id)));
        Assert.Equal(0, await fx.S.Db.StockMovements.CountAsync(m => m.DocumentId == a.Id || m.DocumentId == b.Id));
        await fx.S.Db.StockDocumentLines.Where(l => l.DocumentId == b.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Quantity, 20m));
        fx.S.Db.ChangeTracker.Clear();
        await fx.S.Stock.PostDocumentAsync(fx.H.Approver, a.Id);
        await fx.S.Stock.CancelDocumentAsync(fx.H.Preparer, b.Id, "Không nhận nữa");   // cancelling releases the reservation
        await Raw(20);
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => Raw(1)));
    }

    [Fact]
    public async Task PurchaseReturnIssues_CannotExceedTheQuantityBought_EvenBeforeACreditNote()
    {
        using var fx = await SetupAsync();
        var invoice = await PostedPurchaseOfAdditiveAsync(fx);              // 100 kg bought
        await fx.Inv.CreatePurchaseReturnIssueAsync(fx.H.Preparer, invoice.Id, 1, 60, D(3, 10));
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreatePurchaseReturnIssueAsync(fx.H.Preparer, invoice.Id, 1, 60, D(3, 11))));
        await fx.Inv.CreatePurchaseReturnIssueAsync(fx.H.Preparer, invoice.Id, 1, 40, D(3, 11));
    }

    // ------------------------------------------------------------------ Đợt 2: tín dụng theo cam kết

    private static async Task<Hak.Application.Cycles.SalesOrderCheck> CheckAsync(Fx fx, Guid orderId)
        => await new Hak.Application.Cycles.SalesOrderCheckService(fx.S.Db, fx.S.Settlement, fx.H.Clock, fx.S.Dryness).CheckAsync(fx.H.Approver, orderId);

    [Fact]
    public async Task Credit_TwoOrdersOf50_WithOpenBalance20_AreComparedAgainstTheLimitTogether_AndMovingStepsAreNotDoubleCounted()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await s.Setup.UpdatePartnerCommercialAsync(h.Admin, h.Partner["KH01"], "12 Lê Duẩn", "0236 111 222", "kh01@example.vn", 100_000_000, 30);
        await h.PostedAsync(s, new Hak.Application.Accounting.VoucherInput(Hak.Domain.Vouchers.VoucherTypes.General, D(3, 1), D(3, 1), "Bán chịu",
            [new(h.Acc["AR"], Hak.Domain.Vouchers.Side.Debit, 20_000_000, h.Partner["KH01"], DueDate: D(4, 30)), new(h.Acc["REV"], Hak.Domain.Vouchers.Side.Credit, 20_000_000)]));
        await PostedReceiptAsync(fx, 100, 10_000_000, D(3, 2), fx.Add);

        var orders = Orders(fx);
        async Task<TradeOrder> NewOrder(string note) => await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 5), note, [new OrderLineInput(fx.Add, 10, 5_000_000)]));
        var a = await NewOrder("Đơn A");
        var b = await NewOrder("Đơn B");
        Assert.Equal(70_000_000m, (await CheckAsync(fx, a.Id)).BalanceAfter);           // 20 + 50
        await orders.ConfirmAsync(h.Approver, a.Id);

        var checkB = await CheckAsync(fx, b.Id);                                         // 20 + A's undelivered 50 + 50 = 120, not 70
        Assert.Equal((20_000_000m, 0m, 50_000_000m, 50_000_000m, 120_000_000m, true),
            (checkB.CurrentBalance, checkB.DeliveredNotInvoiced, checkB.OpenCommitments, checkB.OrderValue, checkB.BalanceAfter, checkB.ExceedsCredit));
        Assert.Equal(ErrorCodes.CreditLimitExceeded, await CodeOf(() => orders.ConfirmAsync(h.Approver, b.Id)));

        // Deliver 4 of A: the delivered part moves from "commitment" to "delivered, not invoiced"; the total stays 120.
        var issue = await fx.S.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, D(3, 6), D(3, 6), "Giao A",
            [new StockLineInput(fx.Add, 4, CounterAccountId: fx.A("COGS"), OrderId: a.Id, OrderLineNo: 1)], PartnerId: h.Partner["KH01"]));
        await fx.S.Stock.PostDocumentAsync(h.Approver, issue.Id);
        checkB = await CheckAsync(fx, b.Id);
        Assert.Equal((0m, 20_000_000m, 30_000_000m, 120_000_000m), (checkB.CurrentBalance - 20_000_000m, checkB.DeliveredNotInvoiced, checkB.OpenCommitments, checkB.BalanceAfter));

        // Invoicing it moves the 20 into the receivable: still 120.
        var inv = await fx.Inv.CreateAsync(h.Preparer, new InvoiceInput(InvoiceKind.Sales, h.Partner["KH01"], "1C26T", "300", D(3, 7), D(3, 7), "Bán A đợt 1",
            [new InvoiceLineInput(5_000_000, issue.Id, 1)], DueDate: D(4, 30)));
        await fx.Inv.PostAsync(h.Approver, inv.Id);
        checkB = await CheckAsync(fx, b.Id);
        Assert.Equal((40_000_000m, 0m, 30_000_000m, 120_000_000m), (checkB.CurrentBalance, checkB.DeliveredNotInvoiced, checkB.OpenCommitments, checkB.BalanceAfter));
    }

    [Fact]
    public async Task Credit_BdmtLine_UsesTheAverageDrynessOfStock_AndFallsBackToMtWithANote()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await s.Setup.UpdatePartnerCommercialAsync(h.Admin, h.Partner["KH01"], "12 Lê Duẩn", "0236 111 222", "kh01@example.vn", 100_000_000, 30);
        var orders = Orders(fx);
        var order = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 5), "Bán dăm",
            [new OrderLineInput(fx.Dam, 10, 4_000_000, PricingBasis.Bdmt)]));
        var none = await CheckAsync(fx, order.Id);                                       // no stock, no dryness: MT × price as an upper bound
        Assert.Equal(40_000_000m, none.OrderValue);
        Assert.NotNull(none.EstimateNote);

        var receipt = await PostedReceiptAsync(fx, 100, 150_000_000, D(3, 2), fx.Dam);
        await fx.S.Dryness.RecordAsync(h.Preparer, new MeasurementInput(receipt.Id, 1, 50m, EvidenceType.InternalReport, "PĐ-1", D(3, 3)));
        var withDryness = await CheckAsync(fx, order.Id);                                // 10 MT × 50% × 4,000,000
        Assert.Equal(20_000_000m, withDryness.OrderValue);
        Assert.Null(withDryness.EstimateNote);
    }

    // ------------------------------------------------------------------ Đợt 2: giữ hàng

    [Fact]
    public async Task Reservation_TwoOrdersOf70_OnStockOf100_SecondGetsWhatIsLeftAndTheRestWaits_WithoutJournalOrStockMovement()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 2), fx.Add);
        var orders = Orders(fx);
        async Task<TradeOrder> NewOrder(decimal qty) => await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH02"], D(3, 5), "Bán phụ gia", [new OrderLineInput(fx.Add, qty, 100_000)]));
        var journals = await JournalsAsync(fx);
        var movements = await s.Db.StockMovements.CountAsync();

        var a = await orders.ConfirmAsync(h.Approver, (await NewOrder(70)).Id);
        var b = await orders.ConfirmAsync(h.Approver, (await NewOrder(70)).Id);
        var listed = await s.Reservations.ListAsync(h.Approver);
        Assert.Equal(70m, listed.Single(r => r.OrderId == a.Id).Quantity);
        Assert.Equal(30m, listed.Single(r => r.OrderId == b.Id).Quantity);          // only what was left; 40 waits
        var av = await s.Reservations.AvailabilityAsync(h.Approver, fx.Add);
        Assert.Equal((100m, 100m, 0m), (av.OnHand, av.Reserved, av.Available));
        Assert.Equal(journals, await JournalsAsync(fx));                              // reserving books nothing and moves no stock
        Assert.Equal(movements, await s.Db.StockMovements.CountAsync());

        // A third order sees the stock as taken.
        var c = await NewOrder(10);
        var check = await new Hak.Application.Cycles.SalesOrderCheckService(s.Db, s.Settlement, h.Clock, s.Dryness).CheckAsync(h.Approver, c.Id);
        Assert.Equal((100m, 100m, 10m), (check.Lines[0].OnHand, check.Lines[0].ReservedByOthers, check.Lines[0].Shortfall));

        // Releasing A frees 70; topping B up reserves the 40 that was waiting, and nothing is left waiting.
        await s.Reservations.ReleaseAsync(h.Preparer, listed.Single(r => r.OrderId == a.Id).Id, "Khách hoãn nhận");
        var top = await s.Reservations.ReserveAsync(h.Preparer, b.Id);
        var line = Assert.Single(top.Lines);
        Assert.Equal((30m, 40m, 0m), (line.AlreadyReserved, line.NewlyReserved, line.Waiting));
        Assert.False(top.AnyWaiting);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Reservations.ReleaseAsync(h.Preparer, listed.Single(r => r.OrderId == b.Id).Id, " ")));
    }

    [Fact]
    public async Task Reservation_TwoUsersConfirmingOrdersAtTheSameTime_NeverReserveMoreThanTheStock()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 2), fx.Add);
        var orders = Orders(fx);
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
            ids.Add((await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH02"], D(3, 5), "Bán phụ gia", [new OrderLineInput(fx.Add, 70, 100_000)]))).Id);

        using var s1 = h.NewScope();
        using var s2 = h.NewScope();
        var (ok, failed) = await ConcurrencyTests.RaceAsync(
            () => new OrderService(s1.Db, s1.Audit, h.Clock).ConfirmAsync(h.Approver, ids[0]),
            () => new OrderService(s2.Db, s2.Audit, h.Clock).ConfirmAsync(h.Approver, ids[1]));
        Assert.True(ok >= 1, string.Join("; ", failed.Select(f => f.Message)));
        Assert.All(failed, f => Assert.IsType<DomainException>(f));

        using var check = h.NewScope();
        var reserved = (await check.Reservations.ListAsync(h.Approver)).Sum(r => r.Remaining);
        var av = await check.Reservations.AvailabilityAsync(h.Approver, fx.Add);
        Assert.Equal(100m, av.OnHand);
        Assert.True(reserved <= 100m, $"Giữ {reserved} trên tồn 100.");
        Assert.Equal(reserved, av.Reserved);
        if (ok == 2) Assert.Equal(100m, reserved);                       // 70 + phần còn lại 30, phần thiếu chờ
    }

    [Fact]
    public async Task Reservation_IsConsumedByDeliveryAgainstTheOrder_AndReleasedWhenTheOrderIsCancelledOrClosed()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 2), fx.Add);
        var orders = Orders(fx);
        async Task<TradeOrder> Confirmed(decimal qty) => await orders.ConfirmAsync(h.Approver, (await orders.CreateAsync(h.Preparer,
            new OrderInput(InvoiceKind.Sales, h.Partner["KH02"], D(3, 5), "Bán phụ gia", [new OrderLineInput(fx.Add, qty, 100_000)]))).Id);
        var a = await Confirmed(40);
        var issue = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, D(3, 6), D(3, 6), "Giao 25",
            [new StockLineInput(fx.Add, 25, CounterAccountId: fx.A("COGS"), OrderId: a.Id, OrderLineNo: 1)], PartnerId: h.Partner["KH02"]));
        await s.Stock.PostDocumentAsync(h.Approver, issue.Id);
        var left = Assert.Single(await s.Reservations.ListAsync(h.Approver, a.Id));
        Assert.Equal((40m, 25m, 15m), (left.Quantity, left.Consumed, left.Remaining));       // the delivered part no longer holds stock
        var av = await s.Reservations.AvailabilityAsync(h.Approver, fx.Add);
        Assert.Equal((75m, 15m, 60m), (av.OnHand, av.Reserved, av.Available));

        await orders.CloseAsync(h.Preparer, a.Id, "Khách nhận đủ phần cần");
        Assert.Empty(await s.Reservations.ListAsync(h.Approver, a.Id));                       // closing releases what is left
        Assert.Equal("Released", Assert.Single(await s.Reservations.ListAsync(h.Approver, a.Id, activeOnly: false)).Status);

        var b = await Confirmed(30);
        Assert.Equal(30m, (await s.Reservations.AvailabilityAsync(h.Approver, fx.Add)).Reserved);
        await orders.CancelAsync(h.Preparer, b.Id, "Khách hủy");
        Assert.Equal(0m, (await s.Reservations.AvailabilityAsync(h.Approver, fx.Add)).Reserved);
    }

    // ------------------------------------------------------------------ Đợt 3: đối chiếu lũy kế, dung sai, duyệt sai lệch

    private async Task<(TradeOrder Order, StockDocument Receipt)> ConfirmedPurchaseWithReceiptAsync(Fx fx, decimal orderQty, decimal receiptQty, decimal price = 50_000)
    {
        var (h, s) = (fx.H, fx.S);
        var orders = Orders(fx);
        var order = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Purchase, h.Partner["NCC01"], D(3, 1), "Mua phụ gia", [new OrderLineInput(fx.Add, orderQty, price)]));
        await orders.ConfirmAsync(h.Approver, order.Id);
        var receipt = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Wh, D(3, 2), D(3, 2), "Nhập theo đơn",
            [new StockLineInput(fx.Add, receiptQty, 0, fx.A("GRNI"), OrderId: order.Id, OrderLineNo: 1)], PartnerId: h.Partner["NCC01"]));
        return (order, receipt);
    }

    [Fact]
    public async Task PurchaseMatch_IsCumulativeAcrossInvoices_OnTheSameReceiptLine()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var inspections = new Hak.Application.Cycles.GoodsInspectionService(s.Db, s.Audit, h.Clock);
        var match = new Hak.Application.Cycles.PurchaseMatchService(s.Db);
        var (_, receipt) = await ConfirmedPurchaseWithReceiptAsync(fx, 100, 100);
        await inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 2), "Ban kiểm nghiệm", null, [new(1, 90)]));      // 90 of 100 accepted

        var first = await fx.Inv.CreateAsync(h.Preparer, Purchase(fx, "0000500", new InvoiceLineInput(50_000, receipt.Id, 1, 50, VatRate: 10)));
        Assert.True((await match.MatchAsync(h.Auditor, first.Id)).Matched);                                                // 50 ≤ 90 on its own
        var second = await fx.Inv.CreateAsync(h.Preparer, Purchase(fx, "0000501", new InvoiceLineInput(50_000, receipt.Id, 1, 50, VatRate: 10)));
        var m2 = await match.MatchAsync(h.Auditor, second.Id);                                                             // 50 + 50 = 100 > 90 accepted
        var flag = Assert.Single(m2.Flags, f => f.Blocking);
        Assert.Equal("OVER_ACCEPTED", flag.Code);
        Assert.Equal(100m, m2.Lines[0].CumulativeInvoicedOnReceipt);
        Assert.False((await match.MatchAsync(h.Auditor, first.Id)).Matched);                                               // the earlier invoice now sees the total too
    }

    [Fact]
    public async Task PurchaseMatch_PriceTolerance_PolicyIsVersioned_PartnerBeatsCompany_AndChangesTheSnapshot()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var match = new Hak.Application.Cycles.PurchaseMatchService(s.Db);
        var variance = new Hak.Application.Cycles.PurchaseVarianceService(s.Db, s.Audit, h.Clock);
        var (_, receipt) = await ConfirmedPurchaseWithReceiptAsync(fx, 100, 100, price: 50_000);
        var inv = await fx.Inv.CreateAsync(h.Preparer, Purchase(fx, "0000510", new InvoiceLineInput(50_500, receipt.Id, 1, VatRate: 10)));   // +1% over the order price
        await fx.Inv.PostAsync(h.Approver, inv.Id);

        var strict = await match.MatchAsync(h.Auditor, inv.Id);
        Assert.Contains(strict.Flags, f => f.Code == "PRICE_DIFF" && f.Blocking);
        Assert.Null(strict.PolicyVersion);

        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => variance.SetPolicyAsync(h.Auditor, new(Hak.Domain.Cycles.MatchPolicyScope.Company, null, 2, 0, 0, D(1, 1)))));
        var v1 = await variance.SetPolicyAsync(h.Admin, new(Hak.Domain.Cycles.MatchPolicyScope.Company, null, 2, 0, 0, D(1, 1), "Dung sai 2%"));
        Assert.Equal(1, v1.Version);
        var within = await match.MatchAsync(h.Auditor, inv.Id);
        Assert.True(within.Matched);
        Assert.Contains(within.Flags, f => f.Code == "PRICE_WITHIN_TOLERANCE" && !f.Blocking);
        Assert.NotEqual(strict.SnapshotHash, within.SnapshotHash);

        // A stricter partner policy (new version for that supplier) wins over the company's.
        await variance.SetPolicyAsync(h.Admin, new(Hak.Domain.Cycles.MatchPolicyScope.Partner, h.Partner["NCC01"], 0.5m, 0, 0, D(1, 1)));
        Assert.False((await match.MatchAsync(h.Auditor, inv.Id)).Matched);
        var v2 = await variance.SetPolicyAsync(h.Admin, new(Hak.Domain.Cycles.MatchPolicyScope.Company, null, 3, 0, 0, D(1, 1)));
        Assert.Equal(2, v2.Version);
        Assert.Equal(1, (await variance.ListPoliciesAsync(h.Auditor)).Count(p => p.Scope == "Company" && p.IsActive));
    }

    [Fact]
    public async Task VarianceApproval_IsBoundToTheSourceData_AndSourceChangesRequireApprovalAgain()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var inspections = new Hak.Application.Cycles.GoodsInspectionService(s.Db, s.Audit, h.Clock);
        var variance = new Hak.Application.Cycles.PurchaseVarianceService(s.Db, s.Audit, h.Clock);
        var (_, receipt) = await ConfirmedPurchaseWithReceiptAsync(fx, 100, 100);
        var inv = await fx.Inv.CreateAsync(h.Preparer, Purchase(fx, "0000520", new InvoiceLineInput(50_000, receipt.Id, 1, VatRate: 10)));
        await fx.Inv.PostAsync(h.Approver, inv.Id);
        var bb = await inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 2), "Ban kiểm nghiệm", null, [new(1, 90)]));

        Assert.Equal(ErrorCodes.PaymentMatchFailed, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null)));
        var approval = await variance.ApproveAsync(h.Approver, inv.Id, "NCC giảm giá kỳ sau");
        Assert.Equal(approval.Id, (await variance.CurrentAsync(h.Auditor, inv.Id))!.Id);
        var queue = Assert.Single(await new Hak.Application.Cycles.PaymentAcceptanceService(s.Db, s.PaymentOrders, s.Settlement, s.Audit, h.Clock).PayableQueueAsync(h.Auditor, D(3, 31)));
        Assert.True(queue.VarianceApproved);

        // The source changes (a different inspection result): the approval no longer matches and acceptance is refused again.
        await inspections.CancelAsync(h.Preparer, bb.Id, "Kiểm nghiệm lại");
        await inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 3), "Ban kiểm nghiệm 2", null, [new(1, 80)]));
        Assert.False((await variance.CurrentAsync(h.Auditor, inv.Id))!.StillValid);
        Assert.Equal(ErrorCodes.PaymentMatchFailed, await CodeOf(() => s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null)));
        await variance.ApproveAsync(h.Approver, inv.Id, "Duyệt lại theo kết quả kiểm nghiệm mới");
        Assert.Equal(Hak.Domain.Cycles.VarianceApprovalStatus.Superseded.ToString(), (await s.Db.PurchaseVarianceApprovals.AsNoTracking().SingleAsync(a => a.Id == approval.Id)).Status.ToString());
        await s.PaymentAcceptances.AcceptAsync(h.Preparer, inv.Id, null);
    }

    [Fact]
    public async Task OrderReceiving_KeepsWhatActuallyArrived_EvenWhenPartIsRejectedOrWaitsForQc()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var inspections = new Hak.Application.Cycles.GoodsInspectionService(s.Db, s.Audit, h.Clock);
        var receiving = new Hak.Application.Cycles.PurchaseReceivingService(s.Db);
        var (order, receipt) = await ConfirmedPurchaseWithReceiptAsync(fx, 100, 60);

        var before = Assert.Single((await receiving.ForOrderAsync(h.Auditor, order.Id)).Lines);
        Assert.Equal((100m, 60m, 60m, 0m, 0m, 40m), (before.Ordered, before.Arrived, before.Accepted, before.Rejected, before.PendingQc, before.NotYetArrived));
        await inspections.CreateAsync(h.Preparer, new(receipt.Id, D(3, 2), "Ban kiểm nghiệm", "5 kg hỏng", [new(1, 55)]));
        var after = Assert.Single((await receiving.ForOrderAsync(h.Auditor, order.Id)).Lines);
        Assert.Equal((60m, 55m, 5m, 0m), (after.Arrived, after.Accepted, after.Rejected, after.PostedToStock));   // the 5 rejected still count as arrived
    }

    [Fact]
    public async Task OrderWorklist_SortsBeforePaging_OldOrdersAreNotDroppedByARecencyCut()
    {
        // Kiểm tra 25/10/2026 (F06): trước đây lấy "N đơn mới nhất" rồi mới xếp ưu tiên nên đơn cũ (lẽ ra lên đầu) biến mất khi có nhiều đơn.
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var flow = new Hak.Application.Cycles.OrderFlowService(s.Db, Orders(fx), h.Clock);
        var orders = Orders(fx);
        var made = new List<Hak.Domain.Trade.TradeOrder>();
        for (var day = 1; day <= 5; day++)
            made.Add(await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, day), $"Đơn ngày {day}", [new OrderLineInput(fx.Add, 1, 100_000)])));

        var page1 = await flow.WorklistAsync(h.Auditor, InvoiceKind.Sales, skip: 0, take: 2);
        var page2 = await flow.WorklistAsync(h.Auditor, InvoiceKind.Sales, skip: 2, take: 2);
        var page3 = await flow.WorklistAsync(h.Auditor, InvoiceKind.Sales, skip: 4, take: 2);
        Assert.Equal((5, true, true, false), (page1.Total, page1.HasMore, page2.HasMore, page3.HasMore));
        // Đơn cũ nhất (tuổi lớn nhất) đứng đầu dù chỉ lấy 2 dòng; ba trang không trùng, không sót.
        Assert.Equal(made[0].Id, page1.Items[0].OrderId);
        var all = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(r => r.OrderId).ToList();
        Assert.Equal(made.Select(o => o.Id).OrderBy(x => x), all.OrderBy(x => x));
        Assert.Equal(5, all.Distinct().Count());
        // Chiều mua không thấy đơn bán.
        Assert.Empty((await flow.WorklistAsync(h.Auditor, InvoiceKind.Purchase)).Items);
    }

    // ------------------------------------------------------------------ Đợt 4: hồ sơ chu trình và bốn trục trạng thái

    [Fact]
    public async Task OrderFlow_FourAxes_NextActionAndLinkedDocuments_FollowTheOrderFromDraftToClosed()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var flow = new Hak.Application.Cycles.OrderFlowService(s.Db, Orders(fx), h.Clock);
        var orders = Orders(fx);
        var order = await orders.CreateAsync(h.Preparer, new OrderInput(InvoiceKind.Sales, h.Partner["KH01"], D(3, 5), "Bán phụ gia", [new OrderLineInput(fx.Add, 10, 100_000)], ExpectedDate: D(3, 20)));
        static string State(Hak.Application.Cycles.OrderFlow f, string axis) => f.Axes.Single(a => a.Axis == axis).State;

        var f0 = await flow.FlowAsync(h.Auditor, order.Id);                                           // draft
        Assert.Equal(("None", "None", "None", "None"), (State(f0, "Approval"), State(f0, "Fulfilment"), State(f0, "Invoicing"), State(f0, "Payment")));
        Assert.Equal(("CONFIRM", h.PreparerId), (f0.Next!.Code, f0.Next.OwnerId));

        await orders.ConfirmAsync(h.Approver, order.Id);
        var f1 = await flow.FlowAsync(h.Auditor, order.Id);
        Assert.Equal(("Full", "DELIVER", h.ApproverId, D(3, 20)), (State(f1, "Approval"), f1.Next!.Code, f1.Next.OwnerId, f1.Next.Due));

        async Task<StockDocument> Deliver(decimal qty, int day)
        {
            var d = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(StockDocumentType.Issue, fx.Wh, D(3, day), D(3, day), "Giao hàng",
                [new StockLineInput(fx.Add, qty, CounterAccountId: fx.A("COGS"), OrderId: order.Id, OrderLineNo: 1)], PartnerId: h.Partner["KH01"]));
            return await s.Stock.PostDocumentAsync(h.Approver, d.Id);
        }
        var issue1 = await Deliver(4, 6);
        var f2 = await flow.FlowAsync(h.Auditor, order.Id);                                           // delivered 4 of 10
        Assert.Equal(("Partial", "DELIVER"), (State(f2, "Fulfilment"), f2.Next!.Code));
        Assert.Contains(f2.Documents, d => d.Kind == "Phiếu xuất" && d.Id == issue1.Id);

        var issue2 = await Deliver(6, 7);
        var f3 = await flow.FlowAsync(h.Auditor, order.Id);                                           // delivered in full, nothing invoiced yet
        Assert.Equal(("Full", "None", "INVOICE"), (State(f3, "Fulfilment"), State(f3, "Invoicing"), f3.Next!.Code));

        var inv = await fx.Inv.CreateAsync(h.Preparer, new InvoiceInput(InvoiceKind.Sales, h.Partner["KH01"], "1C26T", "700", D(3, 8), D(3, 8), "Bán phụ gia",
            [new InvoiceLineInput(100_000, issue1.Id, 1), new InvoiceLineInput(100_000, issue2.Id, 1)], DueDate: D(4, 8)));
        await fx.Inv.PostAsync(h.Approver, inv.Id);
        var f4 = await flow.FlowAsync(h.Auditor, order.Id);                                           // invoiced, not collected
        Assert.Equal(("Full", "None", "COLLECT", D(4, 8)), (State(f4, "Invoicing"), State(f4, "Payment"), f4.Next!.Code, f4.Next.Due));
        Assert.Contains(f4.Documents, d => d.Kind == "Hóa đơn" && d.Id == inv.Id);
        Assert.Contains(f4.Documents, d => d.Kind == "Chứng từ kế toán" && d.Id == inv.VoucherId);

        var line = Phase2Fixture.LineOf(s, inv.VoucherId!.Value, fx.A("AR"));
        var receipt = await Phase2Fixture.PostDocAsync(h, s, Phase2Fixture.Receipt(h, 400_000, D(3, 12), line));
        var f5 = await flow.FlowAsync(h.Auditor, order.Id);                                           // partly collected
        Assert.Equal(("Partial", "COLLECT"), (State(f5, "Payment"), f5.Next!.Code));
        Assert.Contains(f5.Documents, d => d.Kind == "Thanh toán / bù trừ" && d.Id == receipt.Id && d.Amount == 400_000m);

        await Phase2Fixture.PostDocAsync(h, s, Phase2Fixture.Receipt(h, 600_000, D(3, 13), line));
        var f6 = await flow.FlowAsync(h.Auditor, order.Id);
        Assert.Equal(("Full", "CLOSE"), (State(f6, "Payment"), f6.Next!.Code));

        // The worklist lists open orders with their next action; a finished-and-closed order drops out.
        var work = (await flow.WorklistAsync(h.Auditor, InvoiceKind.Sales)).Items;
        Assert.Equal(("CLOSE", order.Number), (Assert.Single(work, w => w.OrderId == order.Id).ActionCode, order.Number));
        await orders.CloseAsync(h.Preparer, order.Id, "Hoàn tất");
        Assert.DoesNotContain((await flow.WorklistAsync(h.Auditor, InvoiceKind.Sales)).Items, w => w.OrderId == order.Id);
        Assert.Equal("Closed", State(await flow.FlowAsync(h.Auditor, order.Id), "Approval"));
        await LedgerInvariants.AssertAsync(fx.S.Db);   // bất biến chéo chu trình: cân Nợ/Có, một nguồn một bút toán, phân bổ không vượt, tồn không âm
    }

    // ------------------------------------------------------------------ Đợt 5: bảng kiểm đóng kỳ

    [Fact]
    public async Task CloseChecklist_SeparatesBlockingFromLegitimateCarryOver_AndCheckedLockRefusesBlocking()
    {
        using var fx = await SetupAsync();
        var (h, s) = (fx.H, fx.S);
        await Phase2Fixture.ConfigureAsync(h, s);
        var check = new Hak.Application.Setup.PeriodCloseChecklistService(s.Db, s.Stock, s.Settlement, s.Assets, s.Loans, fx.Inv, s.Setup, new Hak.Application.Tax.VatOffsetService(s.Db, s.Engine));
        var march = await s.Db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.PeriodNo == 3);
        string Sev(Hak.Application.Setup.PeriodCloseChecklist c, string code) => c.Items.Single(i => i.Code == code).Severity;

        // Goods received but not yet invoiced: GRNI stays on the books and is a legitimate carry-over, not an error.
        await DraftReceiptAsync(fx, 10, D(3, 2), fx.Add);
        var receipt = await PostedReceiptAsync(fx, 20, 1_000_000, D(3, 3), fx.Add);
        var clean = await check.ForPeriodAsync(h.Auditor, march.Id);
        Assert.Equal(("Ok", "CarryOver", "Confirm", "Ok"), (Sev(clean, "INVENTORY_GL"), Sev(clean, "GRNI_CARRYOVER"), Sev(clean, "STOCK_DRAFT"), Sev(clean, "SUBLEDGER_GL")));
        Assert.True(clean.CanLock);
        Assert.Equal(1_000_000m, clean.Items.Single(i => i.Code == "GRNI_CARRYOVER").Amount);
        // Việc cuối kỳ (cần xác nhận): có bộ quy tắc kết chuyển hiệu lực mà kỳ chưa có chứng từ kết chuyển → nhắc, không chặn khóa.
        var set = await s.Closing.CreateRuleSetAsync(h.Admin, "CLOSE-CHK", new(2026, 1, 1), null, [new Hak.Application.Rules.ClosingStepInput("DT", "REV", "RESULT")]);
        await s.Closing.ActivateRuleSetAsync(h.Admin, set.Id);
        var withRules = await check.ForPeriodAsync(h.Auditor, march.Id);
        Assert.Equal("Confirm", Sev(withRules, "CLOSING_VOUCHER"));
        Assert.True(withRules.CanLock);

        // An approved-but-unposted voucher is a blocking error: the checked lock refuses, the checklist says why.
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        await s.Engine.ApproveAsync(h.Approver, v.Id);
        var blocked = await check.ForPeriodAsync(h.Auditor, march.Id);
        Assert.Equal(("Blocking", false), (Sev(blocked, "VOUCHERS_PENDING"), blocked.CanLock));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => check.LockCheckedAsync(h.Auditor, march.Id)));
        Assert.Equal(ErrorCodes.PeriodHasPending, await CodeOf(() => check.LockCheckedAsync(h.Approver, march.Id)));
        Assert.Equal(Hak.Domain.Organization.PeriodStatus.Open, (await s.Db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == march.Id)).Status);

        // Once it is posted the carry-over items do not stop the lock.
        await s.Engine.PostAsync(h.Approver, v.Id);
        await check.LockCheckedAsync(h.Approver, march.Id);
        Assert.Equal(Hak.Domain.Organization.PeriodStatus.Locked, (await s.Db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == march.Id)).Status);
        Assert.Equal("Locked", (await check.ForPeriodAsync(h.Auditor, march.Id)).Status);
    }

    // ------------------------------------------------------------------ Chiết khấu thương mại / chiết khấu thanh toán (làm lại 25/10/2026)

    private static async Task<CommercialInvoice> PostedSaleAsync(Fx fx, string no, decimal qty, DateOnly date, decimal price = 100_000, decimal? pct = null, decimal? amount = null, Guid? partner = null)
    {
        var issue = await PostedIssueAsync(fx, qty, date, fx.Add);
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, partner ?? fx.H.Partner["KH01"], "1C26TBB", no, date, date, "Bán phụ gia",
            [new InvoiceLineInput(price, issue.Id, 1, VatRate: 10, DiscountPercent: pct, DiscountAmount: amount)]));
        return await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
    }

    [Fact]
    public async Task TradeDiscountOnInvoice_ReducesRevenueVatAndReceivable_AndAReturnCarriesTheSameDiscount()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 200, 10_000_000, D(3, 1), fx.Add);
        // 10 kg × 100,000 = 1,000,000; chiết khấu 10% ghi trên hóa đơn → doanh thu 900,000, thuế 90,000, phải thu 990,000.
        var inv = await PostedSaleAsync(fx, "0000401", 10, D(3, 3), pct: 10);
        var line = Assert.Single(inv.Lines);
        Assert.Equal((100_000m, 100_000m, 900_000m, 900_000m, 90_000m), (line.DiscountAmount, line.FunctionalDiscountAmount, line.Amount, line.FunctionalAmount, line.VatAmount));
        Assert.Equal(10m, line.DiscountPercent);
        Assert.Equal(990_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(-90_000m, await fx.Bal("VATOUT"));
        Assert.True((await fx.S.Settlement.ReconcileAsync(fx.H.Auditor, D(12, 31))).IsReconciled);

        // Chiết khấu nhập bằng số tiền: 5 kg × 100,000 = 500,000 trừ 50,000.
        var byAmount = await PostedSaleAsync(fx, "0000402", 5, D(3, 4), amount: 50_000);
        Assert.Equal((450_000m, 45_000m, (decimal?)null), (byAmount.Lines[0].Amount, byAmount.Lines[0].VatAmount, byAmount.Lines[0].DiscountPercent));

        // Nhập cả % và số tiền, hoặc chiết khấu vượt tiền hàng: bị từ chối, không ghi gì.
        var issue = await PostedIssueAsync(fx, 1, D(3, 5), fx.Add);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", "x1", D(3, 5), D(3, 5), "x",
            [new InvoiceLineInput(100_000, issue.Id, 1, DiscountPercent: 5, DiscountAmount: 1000)]))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", "x2", D(3, 5), D(3, 5), "x",
            [new InvoiceLineInput(100_000, issue.Id, 1, DiscountAmount: 100_001)]))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", "x3", D(3, 5), D(3, 5), "x",
            [new InvoiceLineInput(100_000, issue.Id, 1, DiscountPercent: 100.5m)]))));

        // Trả lại 5 / 10 kg của hóa đơn có chiết khấu 10%: giảm doanh thu đúng bằng số đã ghi (5 × 100,000 × 90% = 450,000), thuế 45,000.
        var receipt = await fx.Inv.CreateReturnReceiptAsync(fx.H.Preparer, inv.Id, 1, 5, D(3, 8));
        var credit = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(inv.Id, InvoicePurpose.Return, "1C26TBB", "0000403", D(3, 8), D(3, 8), "Khách trả lại 5 kg",
            [new AdjustmentLineInput(1, receipt.Id, 1)]));
        Assert.Equal((450_000m, 45_000m, 50_000m), (credit.Lines[0].FunctionalAmount, credit.Lines[0].VatAmount, credit.Lines[0].FunctionalDiscountAmount));
        await fx.Inv.PostAsync(fx.H.Approver, credit.Id);
        Assert.Equal(990_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));        // 990,000 + 495,000 (HĐ có chiết khấu số tiền) − 495,000 (hàng trả lại): khoản trả lại bù đúng phần đã ghi
        Assert.Equal(450_000m, await fx.Bal("SALESRET"));                          // doanh thu bị trả lại = 5 kg × 100,000 × 90%, không phải giá chưa chiết khấu
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task TradeDiscountAfterSale_UsesItsOwnAccountWhenMapped_OtherwiseFallsBack_AndKindIsRecorded()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 200, 10_000_000, D(3, 1), fx.Add);
        var a = await PostedSaleAsync(fx, "0000411", 10, D(3, 3));
        var b = await PostedSaleAsync(fx, "0000412", 10, D(3, 4));
        fx.H.Acc["SALESTRD"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "SALESTRD", "Chiết khấu thương mại (thử)")).Id;

        // Chưa ánh xạ vai trò SALES_TRADE_DISCOUNT: chiết khấu thương mại dùng chung TK giảm giá hàng bán.
        var d1 = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(a.Id, InvoicePurpose.Discount, "1C26TBB", "0000413", D(3, 10), D(3, 10), "Chiết khấu đạt doanh số",
            [new AdjustmentLineInput(1, Amount: 100_000)], DiscountKind.Trade));
        Assert.Equal(DiscountKind.Trade, d1.DiscountKind);
        await fx.Inv.PostAsync(fx.H.Approver, d1.Id);
        Assert.Equal(100_000m, await fx.Bal("SALESDISC"));

        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "SALES_TRADE_DISCOUNT", "SALESTRD");
        var d2 = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(b.Id, InvoicePurpose.Discount, "1C26TBB", "0000414", D(3, 11), D(3, 11), "Chiết khấu thương mại",
            [new AdjustmentLineInput(1, Amount: 100_000)], DiscountKind.Trade));
        var d3 = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(b.Id, InvoicePurpose.Discount, "1C26TBB", "0000415", D(3, 11), D(3, 11), "Giảm giá hàng kém chất lượng",
            [new AdjustmentLineInput(1, Amount: 50_000)]));
        Assert.Equal(DiscountKind.Allowance, d3.DiscountKind);
        await fx.Inv.PostAsync(fx.H.Approver, d2.Id);
        await fx.Inv.PostAsync(fx.H.Approver, d3.Id);
        Assert.Equal(100_000m, await fx.Bal("SALESTRD"));
        Assert.Equal(150_000m, await fx.Bal("SALESDISC"));        // 100,000 (trước khi tách TK) + 50,000 giảm giá hàng bán
        Assert.Equal(-1_750_000m, await fx.Bal("SALESREV") + await fx.Bal("SALESTRD") + await fx.Bal("SALESDISC"));   // doanh thu thuần = 2,000,000 − 250,000
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_TiersSettleIntoProRataDraftsPerInvoice_OnlyOnce_AndTheReportSeparatesTheKinds()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 200, 10_000_000, D(3, 1), fx.Add);
        var a = await PostedSaleAsync(fx, "0000421", 10, D(3, 3));       // 1,000,000
        var b = await PostedSaleAsync(fx, "0000422", 20, D(3, 5));       // 2,000,000
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock);
        var tiers = new[] { new AgreementTierInput(2_000_000, 2), new AgreementTierInput(5_000_000, 3) };

        // Bậc thấp hơn không được có tỷ lệ cao hơn; lập thỏa thuận cần quyền duyệt.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-Q1", "x", D(3, 1), D(3, 31),
            [new AgreementTierInput(1000, 5), new AgreementTierInput(2000, 3)]))));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => agreements.CreateAsync(fx.H.Preparer, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-Q1", "x", D(3, 1), D(3, 31), tiers))));
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-Q1", "Chiết khấu doanh số tháng 3", D(3, 1), D(3, 31), tiers));

        var preview = await agreements.PreviewAsync(fx.H.Auditor, agr.Id);
        Assert.Equal((3_000_000m, 2m, 60_000m, 5_000_000m, 3m, false), (preview.EligibleSales, preview.Percent, preview.Amount, preview.NextThreshold, preview.NextPercent, preview.Settled));
        Assert.Equal(new[] { 20_000m, 40_000m }, preview.Invoices.OrderBy(i => i.PostingDate).Select(i => i.Discount));

        // Chưa hết kỳ thỏa thuận thì chưa quyết toán; sai phiên bản bị từ chối.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TBB", "0000430", D(3, 15), D(3, 15))));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion + 5, "1C26TBB", "0000430", D(4, 1), D(4, 1))));
        var run = await agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TBB", "0000430", D(4, 1), D(4, 1));
        Assert.Equal((3_000_000m, 2m, 60_000m), (run.EligibleSales, run.Percent, run.Amount));
        var drafts = await fx.S.Db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => i.Purpose == InvoicePurpose.Discount).OrderBy(i => i.InvoiceNo).ToListAsync();
        Assert.Equal(new[] { "0000430", "0000431" }, drafts.Select(d => d.InvoiceNo));
        Assert.All(drafts, d => Assert.Equal((InvoiceStatus.Draft, DiscountKind.Trade), (d.Status, d.DiscountKind)));
        Assert.Equal(new[] { 20_000m, 40_000m }, drafts.Select(d => d.Lines.Sum(l => l.FunctionalAmount)));
        Assert.Equal(new[] { a.Id, b.Id }, drafts.Select(d => d.AdjustsInvoiceId!.Value));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TBB", "0000440", D(4, 1), D(4, 1))));
        Assert.True((await agreements.PreviewAsync(fx.H.Auditor, agr.Id)).Settled);
        Assert.Equal(3_000_000m, (await agreements.PreviewAsync(fx.H.Auditor, agr.Id)).EligibleSales);   // xem lại sau quyết toán không bị chính chiết khấu của nó làm lệch

        // Hủy quyết toán khi còn nháp: hủy các hóa đơn nháp, làm lại được với số liệu mới.
        await agreements.VoidRunAsync(fx.H.Preparer, agr.Id, "Nhập sai ký hiệu");
        Assert.All(await fx.S.Db.CommercialInvoices.AsNoTracking().Where(i => i.Purpose == InvoicePurpose.Discount).ToListAsync(), d => Assert.Equal(InvoiceStatus.Cancelled, d.Status));
        await agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TBB", "0000450", D(4, 1), D(4, 1));
        var again = await fx.S.Db.CommercialInvoices.Where(i => i.Purpose == InvoicePurpose.Discount && i.Status == InvoiceStatus.Draft).OrderBy(i => i.InvoiceNo).ToListAsync();
        foreach (var d in again) await fx.Inv.PostAsync(fx.H.Approver, d.Id);          // phê duyệt từng dòng không áp cho chiết khấu theo thỏa thuận
        // 60,000 chiết khấu + 6,000 thuế giảm: phải thu 3,300,000 → 3,234,000; doanh thu và thuế đầu ra giảm tương ứng.
        Assert.Equal(3_234_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(60_000m, await fx.Bal("SALESDISC"));
        Assert.Equal(-294_000m, await fx.Bal("VATOUT"));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => agreements.VoidRunAsync(fx.H.Preparer, agr.Id, "Đã ghi sổ")));

        var report = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(1, 1), D(12, 31));
        var row = Assert.Single(report.Rows);
        Assert.Equal(("KH01", 3_000_000m, 0m, 60_000m, 0m, 0m, 2_940_000m), (row.PartnerCode, row.Gross, row.OnInvoiceTradeDiscount, row.AfterSaleTradeDiscount, row.Allowance, row.Returns, row.Net));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_SettledByAdjustingEntry_NoAdjustmentInvoice_ReducesRevenueAndReceivable()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 200, 10_000_000, D(3, 1), fx.Add);
        var a = await PostedSaleAsync(fx, "0000421", 10, D(3, 3));
        await PostedSaleAsync(fx, "0000422", 20, D(3, 5));
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-Q1", "CK tháng 3", D(3, 1), D(3, 31), [new AgreementTierInput(2_000_000, 2)]));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 15))));      // chưa hết kỳ
        var before = await fx.S.Db.CommercialInvoices.AsNoTracking().CountAsync();
        var run = await agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 1));
        Assert.Equal(60_000m, run.Amount);
        Assert.Equal(before, await fx.S.Db.CommercialInvoices.AsNoTracking().CountAsync());                                                              // không sinh hóa đơn điều chỉnh
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 1))));
        var rep0 = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(1, 1), D(12, 31));
        Assert.Equal(0m, rep0.Total.AfterSaleTradeDiscount);                                                                                             // nháp chưa tính

        // Hủy khi còn nháp rồi làm lại; ghi sổ chứng từ.
        await agreements.VoidRunAsync(fx.H.Preparer, agr.Id, "Làm lại");
        Assert.Equal(VoucherStatus.Cancelled, (await fx.S.Db.Vouchers.AsNoTracking().FirstAsync(v => v.Id == run.VoucherId)).Status);
        var run2 = await agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run2.VoucherId!.Value);
        Assert.Equal(3_300_000m - 60_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(60_000m, await fx.Bal("SALESDISC"));
        Assert.Equal(-300_000m, await fx.Bal("VATOUT"));                                                                                                 // thuế không đổi
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => agreements.VoidRunAsync(fx.H.Preparer, agr.Id, "Đã ghi sổ")));
        var rep = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(1, 1), D(12, 31));
        Assert.Equal(60_000m, rep.Total.AfterSaleTradeDiscount);
        Assert.Equal(a.Status, (await fx.S.Db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == a.Id)).Status);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_ProgressiveBands_VersusRetroactive_AndBandEdgesHaveNoGapOrOverlap()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "0000501", 10, D(3, 3), price: 25_000);                               // 250.000 (thang đo nhỏ hơn 1.000 lần ví dụ 250 triệu)
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock);
        var tiers = new[] { new AgreementTierInput(0, 1), new AgreementTierInput(100_000, 2), new AgreementTierInput(200_000, 3) };
        var retro = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-HT", "Hồi tố", D(3, 1), D(3, 31), tiers));
        var prog = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-LT", "Lũy tiến", D(3, 1), D(3, 31), tiers, Progressive: true));

        var pr = await agreements.PreviewAsync(fx.H.Auditor, retro.Id);
        var pp = await agreements.PreviewAsync(fx.H.Auditor, prog.Id);
        Assert.Equal((250_000m, 7_500m), (pr.EligibleSales, pr.Amount));                                 // 250.000 × 3%
        Assert.Equal((250_000m, 4_500m), (pp.EligibleSales, pp.Amount));                                 // 1.000 + 2.000 + 1.500
        Assert.Equal(1.8m, pp.Percent);                                                                  // tỷ lệ bình quân thực tế
        Assert.Equal(4_500m, Assert.Single(pp.Invoices).Discount);

        // Biên bậc: đúng ngưỡng không nhận hai lần.
        await agreements.UpdateAsync(fx.H.Approver, prog.Id, prog.RowVersion, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-LT", "Lũy tiến", D(3, 1), D(3, 31),
            [new AgreementTierInput(0, 1), new AgreementTierInput(250_000, 2)], Progressive: true));
        Assert.Equal(2_500m, (await agreements.PreviewAsync(fx.H.Auditor, prog.Id)).Amount);            // toàn bộ 250.000 ở bậc 1; bậc 2 chưa có phần nào

        // Quyết toán lũy tiến bằng chứng từ: số ghi sổ đúng số đã tính.
        var again = await agreements.UpdateAsync(fx.H.Approver, prog.Id, (await fx.S.Db.DiscountAgreements.AsNoTracking().FirstAsync(a => a.Id == prog.Id)).RowVersion,
            new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-LT", "Lũy tiến", D(3, 1), D(3, 31), tiers, Progressive: true));
        var eng = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var run = await eng.SettleAsync(fx.H.Preparer, prog.Id, again.RowVersion, "1C26TBB", "0000510", D(4, 1), D(4, 1));
        Assert.Equal(4_500m, run.Amount);
        Assert.Equal(4_500m, (await fx.S.Db.CommercialInvoiceLines.AsNoTracking().Where(l => fx.S.Db.CommercialInvoices.Any(i => i.Id == l.InvoiceId && i.Purpose == InvoicePurpose.Discount)).ToListAsync()).Sum(l => l.FunctionalAmount));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_Accrual_ThenSettleByEntry_OffsetsThePayable_AndDoesNotReduceRevenueTwice()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "0000601", 10, D(3, 3), price: 25_000);                                // 250.000
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-TT", "Trích trước", D(3, 1), D(3, 31),
            [new AgreementTierInput(0, 1), new AgreementTierInput(100_000, 2), new AgreementTierInput(200_000, 3)]));
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 10))));       // chưa ánh xạ REBATE_PAYABLE
        fx.H.Acc["REBPAY"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "REBPAY", "Phải trả chiết khấu (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "REBATE_PAYABLE", "REBPAY");

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 10))));      // ngoài kỳ
        var a1 = await agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 10));
        Assert.Equal((7_500m, 7_500m), (a1.Estimated, a1.Adjustment));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 11))));   // còn nháp chưa ghi sổ
        await PostVoucherAsync(fx, a1.VoucherId);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 11))));      // không đổi gì

        await PostedSaleAsync(fx, "0000602", 10, D(3, 20), price: 25_000);                               // doanh số 500.000 → 3% = 15.000
        var a2 = await agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 25));
        Assert.Equal((15_000m, 7_500m), (a2.Estimated, a2.Adjustment));
        await PostVoucherAsync(fx, a2.VoucherId);
        Assert.Equal(15_000m, await fx.Bal("SALESDISC"));
        Assert.Equal(-15_000m, await fx.Bal("REBPAY", fx.H.Partner["KH01"]));
        Assert.Equal(550_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));                                // trích trước chưa đụng công nợ

        var run = await agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 1));
        Assert.Equal((15_000m, 15_000m), (run.Amount, run.AccruedAmount));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        Assert.Equal(0m, await fx.Bal("REBPAY", fx.H.Partner["KH01"]));                                  // bù trừ hết
        Assert.Equal(535_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));                                // công nợ giảm đúng 15.000
        Assert.Equal(15_000m, await fx.Bal("SALESDISC"));                                                // giảm trừ doanh thu vẫn 15.000, không gấp đôi
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_Accrual_ThenSettleByAdjustmentInvoices_ReversesTheAccrual()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "0000611", 10, D(3, 3), price: 25_000);                                // 250.000 → 3% = 7.500
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "CK-HN", "Hoàn nhập", D(3, 1), D(3, 31),
            [new AgreementTierInput(0, 1), new AgreementTierInput(100_000, 2), new AgreementTierInput(200_000, 3)]));
        fx.H.Acc["REBPAY"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "REBPAY", "Phải trả chiết khấu (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "REBATE_PAYABLE", "REBPAY");
        var a1 = await agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 31));
        await PostVoucherAsync(fx, a1.VoucherId);

        var run = await agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TBB", "0000620", D(4, 1), D(4, 1));
        var st = await agreements.StatementAsync(fx.H.Auditor, agr.Id);                                    // bảng kê 01/BK-ĐCTT: hóa đơn gốc ↔ hóa đơn điều chỉnh
        var row = Assert.Single(st.Rows);
        Assert.Equal(("Hóa đơn điều chỉnh", 250_000m, 7_500m, "0000620", 750m), (st.Mode, row.Sales, row.Discount, row.AdjustmentNo, row.AdjustmentVat));
        Assert.Equal((7_500m, 250_000m), (st.TotalDiscount, st.TotalSales));
        Assert.Equal((7_500m, 7_500m), (run.Amount, run.AccruedAmount));
        Assert.Null(run.AccrualReversalVoucherId);
        Assert.Equal(DiscountAccrualSettlementMode.PerInvoice, run.AccrualSettlementMode);
        foreach (var d in await fx.S.Db.CommercialInvoices.Where(i => i.Purpose == InvoicePurpose.Discount && i.Status == InvoiceStatus.Draft).ToListAsync()) await fx.Inv.PostAsync(fx.H.Approver, d.Id);
        Assert.Equal(0m, await fx.Bal("REBPAY", fx.H.Partner["KH01"]));
        Assert.Equal(7_500m, await fx.Bal("SALESDISC"));                                                 // 7.500 trích trước − 7.500 hoàn nhập + 7.500 hóa đơn điều chỉnh
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_PurchaseSide_AccrualToReceivable_ThenSettleByEntry_ReducesPayableAndCost()
    {
        using var fx = await SetupAsync();
        await PostedPurchaseOfAdditiveAsync(fx);                                                          // 5.000.000 + VAT 500.000, NCC01, 3/3
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "CK-MUA", "Rebate mua Q1", D(3, 1), D(3, 31), [new AgreementTierInput(0, 2)]));
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 31))));
        fx.H.Acc["REBREC"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "REBREC", "Phải thu chiết khấu NCC (thử)")).Id;
        fx.H.Acc["PURDISC"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "PURDISC", "Giảm giá vốn hàng mua (thử)")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "REBATE_RECEIVABLE", "REBREC");
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PURCHASE_TRADE_DISCOUNT", "PURDISC");

        var a1 = await agreements.AccrueAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(3, 31));
        Assert.Equal(100_000m, a1.Adjustment);                                                            // 2% × 5.000.000
        await PostVoucherAsync(fx, a1.VoucherId);
        Assert.Equal(100_000m, await fx.Bal("REBREC", fx.H.Partner["NCC01"]));                           // phải thu chiết khấu của NCC (Nợ)
        Assert.Equal(-100_000m, await fx.Bal("PURDISC"));                                                 // giảm giá vốn (Có)
        Assert.Equal(-5_500_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));                            // chưa đụng công nợ

        var run = await agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 1));
        Assert.Equal((100_000m, 100_000m), (run.Amount, run.AccruedAmount));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        Assert.Equal(0m, await fx.Bal("REBREC", fx.H.Partner["NCC01"]));
        Assert.Equal(-5_400_000m, await fx.Bal("AP", fx.H.Partner["NCC01"]));                            // phải trả giảm đúng 100.000
        Assert.Equal(-100_000m, await fx.Bal("PURDISC"));                                                 // không ghi giảm giá vốn hai lần
        var rep = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Purchase, D(1, 1), D(12, 31));
        Assert.Equal(100_000m, rep.Total.AfterSaleTradeDiscount);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task DiscountAgreement_BelowTheFirstTier_CannotBeSettled_AndAPurchaseRebateReducesStockAndPayable()
    {
        using var fx = await SetupAsync();
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock);
        await PostedPurchaseOfAdditiveAsync(fx, "0000501");              // 100 kg × 50,000 = 5,000,000 + VAT 10%
        var low = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "MUA-LOW", "Mua trên 10 triệu", D(3, 1), D(3, 31), [new AgreementTierInput(10_000_000, 2)]));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => agreements.SettleAsync(fx.H.Preparer, low.Id, low.RowVersion, "1C26TAA", "0000510", D(4, 1), D(4, 1))));

        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "MUA-Q1", "Mua từ 5 triệu", D(3, 1), D(3, 31), [new AgreementTierInput(5_000_000, 4)]));
        await PostedIssueAsync(fx, 40, D(3, 6), fx.Add);                 // 40 kg đã xuất: phần chiết khấu tương ứng giảm giá vốn
        await agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TAA", "0000510", D(4, 1), D(4, 1));
        var draft = await fx.S.Db.CommercialInvoices.SingleAsync(i => i.Purpose == InvoicePurpose.Discount && i.Status == InvoiceStatus.Draft);
        await fx.Inv.PostAsync(fx.H.Approver, draft.Id);
        // 4% × 5,000,000 = 200,000: còn tồn 60% = 120,000 giảm tồn kho, đã xuất 40% = 80,000 giảm giá vốn; thuế đầu vào giảm 20,000; công nợ giảm 220,000.
        Assert.Equal(3_000_000m - 120_000m, await fx.Bal("INV"));
        Assert.Equal(2_000_000m - 80_000m, await fx.Bal("COGS"));
        Assert.Equal(500_000m - 20_000m, await fx.Bal("VATIN"));
        Assert.Equal(-(5_500_000m - 220_000m), await fx.Bal("AP", fx.H.Partner["NCC01"]));
        Assert.Equal(0m, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        var report = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Purchase, D(1, 1), D(12, 31));
        Assert.Equal((5_000_000m, 200_000m, 4_800_000m), (report.Total.Gross, report.Total.AfterSaleTradeDiscount, report.Total.Net));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task OrderLineDiscount_IsStoredAndCarriedToTheFulfilmentView()
    {
        using var fx = await SetupAsync();
        var orders = Orders(fx);
        var order = await orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Sales, fx.H.Partner["KH01"], D(3, 2), "Đơn có chiết khấu", [new OrderLineInput(fx.Add, 10, 100_000, DiscountPercent: 5)]));
        Assert.Equal(5m, Assert.Single(order.Lines).DiscountPercent);
        Assert.Equal(5m, Assert.Single((await orders.FulfilmentAsync(fx.H.Auditor, order.Id)).Lines).DiscountPercent);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => orders.CreateAsync(fx.H.Preparer, new OrderInput(InvoiceKind.Sales, fx.H.Partner["KH01"], D(3, 2), "x", [new OrderLineInput(fx.Add, 1, 1, DiscountPercent: 101)]))));
    }
}
