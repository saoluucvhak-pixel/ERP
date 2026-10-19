using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>Hợp đồng mua bán: dự thảo → duyệt → đơn kế thừa trừ lùi → phụ lục → mốc thanh toán → báo cáo, cảnh báo.</summary>
public class ContractTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public async Task Contract_Lifecycle_CallOff_Amendment_Milestones_Reports()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        h.Acc["INV"] = (await s.Setup.CreateAccountAsync(h.Admin, "INV", "Hàng tồn kho (thử)")).Id;
        var dam = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "tấn", "INV")).Id;
        var gio = (await s.Stock.CreateItemAsync(h.Admin, "GIO", "Giấy vụn", "tấn", "INV")).Id;
        var contracts = new ContractService(s.Db, s.Audit, h.Clock);
        var orders = new OrderService(s.Db, s.Audit, h.Clock);
        h.Clock.UtcNow = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc);

        // Dự thảo: đối tác đúng vai trò; hợp đồng khung số lượng, 10% chiết khấu.
        var input = new ContractInput(InvoiceKind.Sales, "Hợp đồng dăm gỗ 2026", ContractCategory.Quantity, h.Partner["KH01"], new(2026, 3, 1), new(2026, 6, 30),
            [new(dam, 1000, 2_000_000, 10, 8)], PaymentTerms: "Thanh toán 30 ngày",
            Milestones: [new("Tạm ứng", 30, 540_000_000, new(2026, 3, 10)), new("Nghiệm thu", 70, 1_260_000_000, new(2026, 7, 10))]);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => contracts.CreateAsync(h.Preparer, input with { PartnerId = h.Partner["NCC01"] })));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => contracts.CreateAsync(h.Preparer, input with { ExpirationDate = new(2026, 2, 1) })));
        var c = await contracts.CreateAsync(h.Preparer, input);
        Assert.Equal("CTR-SAL-2026-00001", c.Code);
        Assert.Equal(1_800_000_000m, c.TotalAmount);                       // 1000 × 2.000.000 × 90%
        Assert.Equal(1, (await contracts.CreateAsync(h.Preparer, input with { Kind = InvoiceKind.Sales, Name = "HĐ thứ hai" })).Version);

        // Chưa hiệu lực thì không kế thừa được; duyệt cần người khác, từ chối cần lý do.
        var line = new OrderLineInput(dam, 400, 1_800_000);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => orders.CreateAsync(h.Preparer, Call(h, c.Id, line))));
        await contracts.SubmitAsync(h.Preparer, c.Id);
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => contracts.DecideAsync(h.Approver, c.Id, false, " ")));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => contracts.UpdateAsync(h.Preparer, c.Id, input)));
        Assert.Equal(ContractStatus.Active, (await contracts.DecideAsync(h.Approver, c.Id, true, null)).Status);

        // Đơn kế thừa: sai đối tác / ngoài hiệu lực / vật tư ngoài hợp đồng / vượt cam kết đều bị chặn.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => orders.CreateAsync(h.Preparer, Call(h, c.Id, line) with { PartnerId = h.Partner["KH02"] })));
        Assert.Equal(ErrorCodes.ContractExpired, await CodeOf(() => orders.CreateAsync(h.Preparer, Call(h, c.Id, line) with { OrderDate = new(2026, 7, 1) })));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => orders.CreateAsync(h.Preparer, Call(h, c.Id, new OrderLineInput(gio, 1, 1)))));
        var o1 = await orders.CreateAsync(h.Preparer, Call(h, c.Id, line));
        Assert.Equal((c.Id, c.Code), (o1.ContractId, o1.ContractNo));
        Assert.Equal(ErrorCodes.ContractLimitExceeded, await CodeOf(() => orders.CreateAsync(h.Preparer, Call(h, c.Id, new OrderLineInput(dam, 700, 1_800_000)))));
        var o2 = await orders.CreateAsync(h.Preparer, Call(h, c.Id, new OrderLineInput(dam, 600, 1_900_000)));      // đúng 1000 tấn

        // Hủy đơn trả lại số lượng; hợp đồng đã có đơn thì không hủy được.
        await orders.CancelAsync(h.Preparer, o2.Id, "Khách đổi kế hoạch");
        var v = await contracts.GetAsync(h.Auditor, c.Id);
        var l1 = Assert.Single(v.Lines);
        Assert.Equal((400m, 600m, 40m), (l1.OrderedQty, l1.RemainingQty, l1.PercentDone));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => contracts.CancelAsync(h.Preparer, c.Id, "x")));

        // Phụ lục: đổi số lượng cam kết theo dòng + gia hạn; mã A01.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => contracts.AmendAsync(h.Preparer, c.Id, new(2026, 4, 1), "Giảm", 0, null, [new(dam, -700)])));  // dưới số đã đặt
        var a = await contracts.AmendAsync(h.Preparer, c.Id, new(2026, 4, 1), "Tăng 200 tấn, gia hạn đến 30/9", 0, new(2026, 9, 30), [new(dam, 200)]);
        Assert.Equal("CTR-SAL-2026-00001-A01", a.Code);
        v = await contracts.GetAsync(h.Auditor, c.Id);
        Assert.Equal((1200m, 2_160_000_000m, new DateOnly(2026, 9, 30)), (v.Lines[0].CommittedQty, v.Header.TotalAmount, v.Header.ExpirationDate));
        await orders.CreateAsync(h.Preparer, Call(h, c.Id, new OrderLineInput(dam, 700, 1_800_000)));                  // trước đây bị chặn, nay đủ chỗ

        // Mốc thanh toán: ghi nhận cộng dồn, không vượt số phải thu; quá hạn được đánh dấu.
        var m1 = v.Milestones[0];
        Assert.Equal("OVERDUE", m1.PaymentStatus);                                                                      // hạn 10/3, hôm nay 15/3
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => contracts.RecordPaymentAsync(h.Preparer, c.Id, m1.Id, 600_000_000, new(2026, 3, 12))));
        await contracts.RecordPaymentAsync(h.Preparer, c.Id, m1.Id, 200_000_000, new(2026, 3, 12));
        Assert.Equal(340_000_000m, (await contracts.MilestoneScheduleAsync(h.Auditor)).First(x => x.MilestoneId == m1.Id).Remaining);

        // Báo cáo: chênh lệch giá (đơn 1.800.000 = giá cam kết sau chiết khấu → 0%), cảnh báo mốc quá hạn, hạn mức.
        var pv = Assert.Single(await contracts.PriceVarianceAsync(h.Auditor, c.Id));
        Assert.Equal((1_800_000m, 0m), (pv.ContractPrice, pv.VariancePercent));
        var alerts = await contracts.AlertsAsync(h.Auditor);
        Assert.Contains(alerts, x => x.Kind == "milestone_overdue");
        Assert.Contains(alerts, x => x.Kind == "limit" && x.ContractCode == c.Code);     // 1100/1200 ≈ 91,67%

        // Tài liệu đính kèm và thanh lý.
        await contracts.AddDocumentAsync(h.Preparer, c.Id, "HD-da-ky.pdf", @"\\server\hopdong\2026\HD-da-ky.pdf", "pdf");
        Assert.Single((await contracts.GetAsync(h.Auditor, c.Id)).Documents);
        Assert.Equal(ContractStatus.Completed, (await contracts.CompleteAsync(h.Preparer, c.Id, "Đã giao đủ")).Status);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => orders.CreateAsync(h.Preparer, Call(h, c.Id, line))));
    }

    [Fact]
    public async Task ValueContract_LimitsByAmount_PurchaseContract_NeedsSupplier()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        h.Acc["INV"] = (await s.Setup.CreateAccountAsync(h.Admin, "INV", "Hàng tồn kho (thử)")).Id;
        var dam = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "tấn", "INV")).Id;
        var contracts = new ContractService(s.Db, s.Audit, h.Clock);
        var orders = new OrderService(s.Db, s.Audit, h.Clock);
        h.Clock.UtcNow = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc);
        var c = await contracts.CreateAsync(h.Preparer, new ContractInput(InvoiceKind.Purchase, "HĐ khung giá trị mua dăm", ContractCategory.Value, h.Partner["NCC01"], new(2026, 1, 1), new(2026, 12, 31), [], TotalAmount: 500_000_000));
        Assert.Equal("CTR-PUR-2026-00001", c.Code);
        await contracts.SubmitAsync(h.Preparer, c.Id); await contracts.DecideAsync(h.Approver, c.Id, true, null);
        OrderInput Buy(decimal qty, decimal price) => new(InvoiceKind.Purchase, h.Partner["NCC01"], new(2026, 3, 15), "Mua theo HĐ khung", [new(dam, qty, price)], ContractId: c.Id);
        await orders.CreateAsync(h.Preparer, Buy(100, 3_000_000));                                                    // 300 triệu
        Assert.Equal(ErrorCodes.ContractLimitExceeded, await CodeOf(() => orders.CreateAsync(h.Preparer, Buy(100, 2_500_000))));   // 550 triệu > 500
        await orders.CreateAsync(h.Preparer, Buy(70, 2_800_000));                                                     // 496 triệu
        Assert.Equal(99.2m, (await contracts.ListAsync(h.Auditor, InvoiceKind.Purchase)).Single().PercentDone);
    }

    private static OrderInput Call(TestHost h, Guid contractId, OrderLineInput line)
        => new(InvoiceKind.Sales, h.Partner["KH01"], new(2026, 3, 15), "Đơn kế thừa hợp đồng", [line], ContractId: contractId);
}
