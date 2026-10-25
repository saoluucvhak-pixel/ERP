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
using Hak.Application.EInvoice;
using Hak.Application.Tax;
using Hak.Domain.Tax;

namespace Hak.Integration.Tests;

/// <summary>Hóa đơn điện tử phát hành từ ERP (docs/12): gốc, điều chỉnh, thay thế, điều chỉnh thông tin, thông báo sai sót, lỗi mạng, từ chối, bất biến.</summary>
public partial class TradeTests
{
    private static async Task<(Fx fx, SimulatedEInvoiceProvider sim, EInvoiceIssueService svc, EInvoiceNoticeService notice)> EInvoiceSetupAsync()
    {
        var fx = await SetupAsync();
        await Phase2Fixture.ConfigureAsync(fx.H, fx.S);
        await fx.S.Db.Companies.Where(c => c.Id == fx.H.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.TaxCode, "0312345678"));
        await PostedReceiptAsync(fx, 500, 25_000_000, D(3, 1), fx.Add);
        var sim = new SimulatedEInvoiceProvider();
        var svc = new EInvoiceIssueService(fx.S.Db, fx.Inv, sim, fx.S.Audit, fx.H.Clock, new EInvoiceOptions("1"));
        return (fx, sim, svc, new EInvoiceNoticeService(fx.S.Db, svc, sim, fx.S.Audit, fx.H.Clock));
    }

    /// <summary>Hóa đơn bán nháp chờ phát hành: 10 kg × 100,000 + VAT 10% = 1,100,000.</summary>
    private static async Task<CommercialInvoice> AwaitingSaleAsync(Fx fx, decimal qty = 10, Guid? partner = null)
    {
        var issue = await PostedIssueAsync(fx, qty, D(3, 3), fx.Add);
        return await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, partner ?? fx.H.Partner["KH01"], "", "", D(3, 3), D(3, 3), "Bán phụ gia",
            [new InvoiceLineInput(100_000, issue.Id, 1, VatRate: 10)], AwaitIssue: true));
    }

    private static async Task<EInvoiceDocumentView> IssuedSaleAsync(Fx fx, EInvoiceIssueService svc, decimal qty = 10)
    {
        var inv = await AwaitingSaleAsync(fx, qty);
        return await svc.IssueAsync(fx.H.Approver, inv.Id, inv.RowVersion);
    }

    [Fact]
    public async Task EInvoice_Issue_AssignsTheRealNumber_PostsByItself_AndFeedsTheThreeWayReconciliation()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var inv = await AwaitingSaleAsync(fx);
        Assert.Equal(InvoiceService.AwaitingSeries, inv.InvoiceSeries);

        // Hóa đơn chờ phát hành không ghi sổ được bằng số tạm (không đi vòng qua nhà cung cấp).
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, inv.Id)));

        // Người lập không tự phát hành hóa đơn của mình; không có nhà cung cấp thì không phát hành được.
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => svc.IssueAsync(fx.H.Preparer, inv.Id)));
        var none = new EInvoiceIssueService(fx.S.Db, fx.Inv, new NullEInvoiceProvider(), fx.S.Audit, fx.H.Clock, new EInvoiceOptions());
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => none.IssueAsync(fx.H.Approver, inv.Id)));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => svc.IssueAsync(fx.H.Approver, inv.Id, inv.RowVersion + 9)));

        var view = await svc.IssueAsync(fx.H.Approver, inv.Id, inv.RowVersion);
        Assert.Equal(("Issued", "Original", "C26TSM", "0000001"), (view.Status, view.Relation, view.Series, view.Number));
        Assert.False(string.IsNullOrEmpty(view.TaxAuthorityCode));
        Assert.Null(view.PostError);
        var posted = await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == inv.Id);
        Assert.Equal((InvoiceStatus.Posted, "C26TSM", "0000001"), (posted.Status, posted.InvoiceSeries, posted.InvoiceNo));
        Assert.Equal(1_100_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        var doc = await fx.S.Db.EInvoiceDocuments.AsNoTracking().SingleAsync();
        Assert.Contains("<SoHoaDon>0000001</SoHoaDon>", doc.XmlContent);
        Assert.Contains("<MaCQT>", doc.XmlContent);
        Assert.Equal(EInvoiceXml.Sha256(doc.XmlContent!), doc.XmlSha256);

        // Phát hành lần hai bị chặn, nhà cung cấp chỉ nhận một lần.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.IssueAsync(fx.H.Approver, inv.Id)));
        Assert.Equal(1, sim.AcceptedCount);

        // Nguồn 1 của đối soát được điền tự động: không còn chênh lệch giữa HĐĐT và sổ.
        var rec = await new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock).ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.Equal(1, rec.Matched);
        Assert.Empty(rec.Differences);
        var msgs = await svc.MessagesAsync(fx.H.Auditor, doc.Id);
        Assert.Equal(new[] { "Queued", "Issue", "Accepted" }, msgs.Select(m => m.Kind));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_NetworkFailure_StaysQueued_AndTheRetryNeverIssuesTwice()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var inv = await AwaitingSaleAsync(fx);
        sim.FailTransientCalls = 1;
        var queued = await svc.IssueAsync(fx.H.Approver, inv.Id);
        Assert.Equal(("Issuing", 1), (queued.Status, queued.Attempts));
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == inv.Id)).Status);
        Assert.Equal(0, sim.AcceptedCount);

        var done = await svc.RetryAsync(fx.H.Approver, queued.Id);       // gửi lại bằng CÙNG khóa chống trùng
        Assert.Equal("Issued", done.Status);
        Assert.Equal(1, sim.AcceptedCount);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.RetryAsync(fx.H.Approver, queued.Id)));   // đã xong: không phát hành lại
        Assert.Equal(1, sim.AcceptedCount);
        Assert.Equal(1, await fx.S.Db.EInvoiceDocuments.CountAsync());
        Assert.Equal(1, await fx.S.Db.JournalEntries.CountAsync(j => j.VoucherNumber.StartsWith("SI")));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_Rejected_KeepsTheDraft_AndReissuingAfterTheFixUsesANewKey()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        await fx.S.Db.BusinessPartners.Where(p => p.Id == fx.H.Partner["KH01"]).ExecuteUpdateAsync(s => s.SetProperty(p => p.TaxCode, "123"));
        var inv = await AwaitingSaleAsync(fx);
        var rejected = await svc.IssueAsync(fx.H.Approver, inv.Id);
        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("MST_NGUOI_MUA", rejected.LastError);
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == inv.Id)).Status);
        Assert.Equal(0m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.IssueAsync(fx.H.Approver, inv.Id)));        // phải dùng Phát hành lại

        await fx.S.Db.BusinessPartners.Where(p => p.Id == fx.H.Partner["KH01"]).ExecuteUpdateAsync(s => s.SetProperty(p => p.TaxCode, "0301234567"));
        var ok = await svc.RetryAsync(fx.H.Approver, rejected.Id);
        Assert.Equal(("Issued", 1_100_000m), (ok.Status, await fx.Bal("AR", fx.H.Partner["KH01"])));
        Assert.Equal(2, await fx.S.Db.EInvoiceOutbox.CountAsync());                                                              // hai khóa, hai lần gửi: lần đầu bị từ chối, lần sau thành công
        Assert.Equal(new[] { EInvoiceOutboxState.Failed, EInvoiceOutboxState.Confirmed }, (await fx.S.Db.EInvoiceOutbox.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync()).Select(o => o.State));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_Adjustment_NeedsAnIssuedOriginal_ThenIssuesAndPostsTheCreditNote()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        var invId = original.InvoiceId!.Value;
        var adj = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(invId, InvoicePurpose.Discount, "TAM", "TAM1", D(3, 20), D(3, 20), "Giảm giá lô hàng",
            [new AdjustmentLineInput(1, Amount: 100_000)], DiscountKind.Trade));
        var view = await svc.IssueAsync(fx.H.Approver, adj.Id);
        Assert.Equal(("Issued", "Adjustment", "Decrease", original.Id), (view.Status, view.Relation, view.Nature, view.RelatedDocumentId));
        var posted = await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == adj.Id);
        Assert.Equal((InvoiceStatus.Posted, "C26TSM", "0000002"), (posted.Status, posted.InvoiceSeries, posted.InvoiceNo));
        Assert.Equal(1_100_000m - 110_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Contains("<HDLienQuan>", (await fx.S.Db.EInvoiceDocuments.AsNoTracking().SingleAsync(d => d.Id == view.Id)).XmlContent);
        Assert.Contains("<SoHoaDon>0000001</SoHoaDon>", (await fx.S.Db.EInvoiceDocuments.AsNoTracking().SingleAsync(d => d.Id == view.Id)).XmlContent);   // tham chiếu số hóa đơn gốc

        // Hóa đơn gốc chưa phát hành qua HAK: không phát hành điều chỉnh được cho tới khi ghi nhận hóa đơn gốc đã phát hành ngoài hệ thống.
        var issue2 = await PostedIssueAsync(fx, 5, D(3, 4), fx.Add);
        var manual = await fx.Inv.PostAsync(fx.H.Approver, (await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "1C26TXX", "0000500", D(3, 4), D(3, 4), "HĐ phát hành ngoài",
            [new InvoiceLineInput(100_000, issue2.Id, 1, VatRate: 10)]))).Id);
        var adj2 = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(manual.Id, InvoicePurpose.Increase, "TAM", "TAM2", D(3, 21), D(3, 21), "Điều chỉnh tăng", [new AdjustmentLineInput(1, Amount: 50_000)]));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.IssueAsync(fx.H.Approver, adj2.Id)));
        var reg = await svc.RegisterExternalAsync(fx.H.Approver, manual.Id);
        Assert.Equal(("Issued", "EXTERNAL", "1C26TXX", "0000500"), (reg.Status, reg.Provider, reg.Series, reg.Number));
        var issued2 = await svc.IssueAsync(fx.H.Approver, adj2.Id);
        Assert.Equal(("Issued", "Increase"), (issued2.Status, issued2.Nature));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_Replacement_ReversesTheOriginalAndPostsTheNewOneAtomically_OnlyAfterTheTaxAuthorityAccepts()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        var origId = original.InvoiceId!.Value;
        Assert.Equal(1_100_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));

        // Sai đơn giá: thay 100,000 → 90,000. Bản nháp thay thế không bị tính "vượt số lượng phiếu xuất" dù hóa đơn gốc vẫn còn hiệu lực.
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, origId, "Sai đơn giá", [new ReplacementLineChange(1, UnitPrice: 90_000)]);
        Assert.Equal((origId, 900_000m), (draft.ReplacesInvoiceId, draft.Lines.Sum(l => l.FunctionalAmount)));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => svc.CreateReplacementDraftAsync(fx.H.Preparer, origId, "Lại", null)));
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == origId)).Status);        // chưa đụng gốc
        var issue = await fx.S.Db.CommercialInvoiceLines.AsNoTracking().Where(l => l.InvoiceId == origId).Select(l => l.StockDocumentId).SingleAsync();
        Assert.Equal(ErrorCodes.OverInvoiced, await CodeOf(() => fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "S", "z9", D(3, 5), D(3, 5), "x",
            [new InvoiceLineInput(1, issue, 1)]))));                                                                                               // người khác vẫn không lập thêm trên cùng phiếu

        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => svc.CreateReplacementDraftAsync(fx.H.Preparer, origId, "Sai đối tượng", null, fx.H.Partner["KH02"])));   // đã có bản thay thế nháp: không lập hai bản
        var journals = await fx.S.Db.JournalEntries.CountAsync();

        var view = await svc.IssueAsync(fx.H.Approver, draft.Id);
        Assert.Equal(("Issued", "Replacement", original.Id), (view.Status, view.Relation, view.RelatedDocumentId));
        Assert.Equal(InvoiceStatus.Reversed, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == origId)).Status);
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == draft.Id)).Status);
        Assert.Equal(990_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(-90_000m, await fx.Bal("VATOUT"));
        Assert.Equal("Replaced", (await svc.ViewAsync(fx.H.Auditor, original.Id)).Status);
        Assert.True(await fx.S.Db.JournalEntries.CountAsync() > journals);

        var rec = await new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock).ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.Empty(rec.Differences);                                                                                                            // gốc Replaced và đã đảo; bản thay thế khớp
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_RejectedReplacement_LeavesTheOriginalAndTheBooksUntouched()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        await fx.S.Db.BusinessPartners.Where(p => p.Id == fx.H.Partner["KH02"]).ExecuteUpdateAsync(s => s.SetProperty(p => p.TaxCode, "12"));
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.InvoiceId!.Value, "Sai người mua", null, fx.H.Partner["KH02"]);
        var journals = await fx.S.Db.JournalEntries.CountAsync();
        var view = await svc.IssueAsync(fx.H.Approver, draft.Id);
        Assert.Equal("Rejected", view.Status);
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == original.InvoiceId)).Status);
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == draft.Id)).Status);
        Assert.Equal(("Issued", journals), ((await svc.ViewAsync(fx.H.Auditor, original.Id)).Status, await fx.S.Db.JournalEntries.CountAsync()));
        Assert.Equal(1_100_000m, await fx.Bal("AR", fx.H.Partner["KH01"]));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_ErrorNotice_AndInformationAdjustment_WriteNothingToTheLedger()
    {
        var (fx, sim, svc, notice) = await EInvoiceSetupAsync(); using var _fx = fx;
        var doc = await IssuedSaleAsync(fx, svc);
        var journals = await fx.S.Db.JournalEntries.CountAsync();

        var info = await svc.AdjustInformationAsync(fx.H.Approver, doc.Id, "Sai địa chỉ người mua", [new InformationChange("Địa chỉ", "Số 1 Lê Lợi", "Số 10 Lê Lợi")]);
        Assert.Equal(("Issued", "Adjustment", "Information", (Guid?)null), (info.Status, info.Relation, info.Nature, info.InvoiceId));
        Assert.Equal(journals, await fx.S.Db.JournalEntries.CountAsync());
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => svc.AdjustInformationAsync(fx.H.Approver, Guid.NewGuid(), "x", [new InformationChange("a", "b", "c")])));

        // Thông báo sai sót cần quyền riêng; số cấp theo bộ mã; cơ quan thuế tiếp nhận loại Hủy ⇒ hóa đơn Cancelled nhưng sổ KHÔNG tự đảo.
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => notice.CreateAsync(fx.H.Preparer, ErrorNoticeKind.Cancel, "Lập nhầm", [new NoticeLineInput(doc.Id, "Lập nhầm")])));
        var n = await notice.CreateAsync(fx.H.Approver, ErrorNoticeKind.Cancel, "Hóa đơn lập nhầm đối tượng", [new NoticeLineInput(doc.Id, "Sai người mua")]);
        Assert.StartsWith("TBSS2026-", n.Number);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => notice.CreateAsync(fx.H.Approver, ErrorNoticeKind.Cancel, "Trùng", [new NoticeLineInput(doc.Id, "Trùng")])));
        var sent = await notice.SendAsync(fx.H.Approver, n.Id, n.RowVersion);
        Assert.Equal("Accepted", sent.Status);
        Assert.Equal("Cancelled", (await svc.ViewAsync(fx.H.Auditor, doc.Id)).Status);
        Assert.Equal(journals, await fx.S.Db.JournalEntries.CountAsync());
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => notice.SendAsync(fx.H.Approver, n.Id, sent.RowVersion)));

        // Đối soát thấy hóa đơn đã hủy trên hệ thống HĐĐT mà sổ còn ghi: yêu cầu kế toán đảo.
        var rec = await new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock).ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.Equal(new[] { TaxDiffType.CancelledButRecorded }, rec.Differences.Select(d => d.Type));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task EInvoice_RejectedNotice_KeepsTheInvoiceIssued()
    {
        var (fx, sim, svc, notice) = await EInvoiceSetupAsync(); using var _fx = fx;
        var doc = await IssuedSaleAsync(fx, svc);
        sim.RejectNotices = true;
        var n = await notice.CreateAsync(fx.H.Approver, ErrorNoticeKind.Cancel, "Hủy", [new NoticeLineInput(doc.Id, "Sai")]);
        var sent = await notice.SendAsync(fx.H.Approver, n.Id, n.RowVersion);
        Assert.Equal(("Rejected", "Issued"), (sent.Status, (await svc.ViewAsync(fx.H.Auditor, doc.Id)).Status));
        sim.FailTransientCalls = 0;
        // Sau khi bị từ chối, hóa đơn có thể đưa vào thông báo mới.
        var n2 = await notice.CreateAsync(fx.H.Approver, ErrorNoticeKind.Explain, "Giải trình", [new NoticeLineInput(doc.Id, "Giải trình sai sót")]);
        Assert.Equal("Draft", n2.Status);
    }

    [Fact]
    public async Task EInvoice_IssuedDocument_IsImmutable_AndThePeriodCannotCloseWhileOneIsStuck()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var doc = await IssuedSaleAsync(fx, svc);
        var tracked = await fx.S.Db.EInvoiceDocuments.SingleAsync(d => d.Id == doc.Id);
        tracked.Number = "9999999";
        await Assert.ThrowsAsync<DomainException>(() => fx.S.Db.SaveChangesAsync());
        fx.S.Db.ChangeTracker.Clear();
        var msg = await fx.S.Db.EInvoiceMessages.FirstAsync();
        msg.Detail = "sửa";
        await Assert.ThrowsAsync<DomainException>(() => fx.S.Db.SaveChangesAsync());
        fx.S.Db.ChangeTracker.Clear();

        var check = new Hak.Application.Setup.PeriodCloseChecklistService(fx.S.Db, fx.S.Stock, fx.S.Settlement, fx.S.Assets, fx.S.Loans, fx.Inv, fx.S.Setup, new VatOffsetService(fx.S.Db, fx.S.Engine));
        var march = await fx.S.Db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.PeriodNo == 3);
        Assert.Equal("Ok", (await check.ForPeriodAsync(fx.H.Auditor, march.Id)).Items.Single(i => i.Code == "EINVOICE_PENDING").Severity);
        sim.FailTransientCalls = 1;
        var stuck = await AwaitingSaleAsync(fx, 5);
        var q = await svc.IssueAsync(fx.H.Approver, stuck.Id);
        Assert.Equal("Issuing", q.Status);
        var blocked = await check.ForPeriodAsync(fx.H.Auditor, march.Id);
        Assert.Equal(("Blocking", false), (blocked.Items.Single(i => i.Code == "EINVOICE_PENDING").Severity, blocked.CanLock));
        await svc.RetryAsync(fx.H.Approver, q.Id);
        Assert.Equal("Ok", (await check.ForPeriodAsync(fx.H.Auditor, march.Id)).Items.Single(i => i.Code == "EINVOICE_PENDING").Severity);
    }
}
