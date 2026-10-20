using Hak.Application.Banking;
using Hak.Application.Equity;
using Hak.Domain.Banking;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Equity;
using Hak.Domain.Vouchers;

namespace Hak.Integration.Tests;

/// <summary>Số tài khoản ngân hàng (đối tượng, công ty) và vốn góp, cổ tức.</summary>
public class EquityTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    private static async Task PostAsync(TestHost h, Scope s, Guid voucherId)
    {
        await s.Engine.SubmitAsync(h.Preparer, voucherId);
        await s.Engine.ApproveAsync(h.Approver, voucherId);
        await s.Engine.PostAsync(h.Approver, voucherId);
    }

    [Fact]
    public async Task BankAccounts_Validate_DefaultMoves_AndPaymentOrderSnapshotsTheDefault()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var banks = new BankAccountService(s.Db, s.Audit, h.Clock);
        h.Clock.UtcNow = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => banks.AddPartnerAccountAsync(h.Admin, h.Partner["NCC01"], new("Vietcombank", null, "12-3", "CÔNG TY NCC 1"))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => banks.AddPartnerAccountAsync(h.Admin, h.Partner["NCC01"], new(" ", null, "0011001234567", "CÔNG TY NCC 1"))));
        var a1 = await banks.AddPartnerAccountAsync(h.Admin, h.Partner["NCC01"], new("Vietcombank", "CN Đà Nẵng", "0011 0012-3456", "CÔNG TY NCC 1"));
        Assert.Equal(("001100123456", true), (a1.AccountNo, a1.IsDefault));                         // chỉ giữ chữ và số; tài khoản đầu tiên là mặc định
        var a2 = await banks.AddPartnerAccountAsync(h.Admin, h.Partner["NCC01"], new("BIDV", null, "9876543210", "CÔNG TY NCC 1", IsDefault: true));
        var list = await banks.PartnerAccountsAsync(h.Auditor, h.Partner["NCC01"]);
        Assert.Equal(["BIDV", "Vietcombank"], list.Select(x => x.BankName).ToArray());
        Assert.Single(list, x => x.IsDefault);
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => banks.AddPartnerAccountAsync(h.Admin, h.Partner["NCC01"], new("BIDV", null, "9876543210", "x x"))));

        // Lệnh chi không nhập tay thụ hưởng → lấy tài khoản mặc định (BIDV) và chụp lại.
        var po = await s.PaymentOrders.CreateAsync(h.Preparer, new PaymentOrderInput(h.Acc["BANK"], h.Partner["NCC01"], 1_000_000, new(2026, 3, 10), "Thanh toán"));
        Assert.Equal(("9876543210", "BIDV"), (po.BeneficiaryAccountNo, po.BeneficiaryBank));
        await banks.SetPartnerAccountAsync(h.Admin, a1.Id, true, null);                              // đổi mặc định sau đó không đổi lệnh cũ
        Assert.Equal("9876543210", (await s.Db.PaymentOrders.FindAsync(po.Id))!.BeneficiaryAccountNo);
        var po2 = await s.PaymentOrders.CreateAsync(h.Preparer, new PaymentOrderInput(h.Acc["BANK"], h.Partner["NCC01"], 1_000, new(2026, 3, 10), "Thanh toán 2", "555666777", "ACB"));
        Assert.Equal(("555666777", "ACB"), (po2.BeneficiaryAccountNo, po2.BeneficiaryBank));         // nhập tay thì giữ nguyên
        Assert.NotEqual(a1.Id, a2.Id);

        // Tài khoản của công ty phải gắn với TK tiền gửi.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => banks.AddCompanyAccountAsync(h.Admin, new(h.Acc["CASH"], "Vietcombank", null, "0011001234567", "CÔNG TY KIỂM THỬ"))));
        var c = await banks.AddCompanyAccountAsync(h.Admin, new(h.Acc["BANK"], "Vietcombank", "CN Đà Nẵng", "0011001234567", "CÔNG TY KIỂM THỬ"));
        Assert.Single(await banks.CompanyAccountsAsync(h.Auditor));
        await banks.SetCompanyAccountActiveAsync(h.Admin, c.Id, false);
        Assert.False((await banks.CompanyAccountsAsync(h.Auditor)).Single().IsActive);
    }

    [Fact]
    public async Task Capital_Dividend_FullFlow_WithPit_Rounding_AndPayments()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        h.Acc["VON"] = (await s.Setup.CreateAccountAsync(h.Admin, "VON", "Vốn góp (thử)")).Id;
        h.Acc["LNST"] = (await s.Setup.CreateAccountAsync(h.Admin, "LNST", "LNST chưa phân phối (thử)")).Id;
        h.Acc["CT"] = (await s.Setup.CreateAccountAsync(h.Admin, "CT", "Cổ tức phải trả (thử)", null, true, true, BalanceDisplay.GrossByPartner, OpenItemNature.Payable)).Id;
        h.Acc["PIT"] = (await s.Setup.CreateAccountAsync(h.Admin, "PIT", "Thuế TNCN phải nộp (thử)")).Id;
        await s.Rules.SetRoleMappingAsync(h.Admin, "CAPITAL_CONTRIBUTED", "VON");
        await s.Rules.SetRoleMappingAsync(h.Admin, "DIVIDEND_SOURCE", "LNST");
        await s.Rules.SetRoleMappingAsync(h.Admin, "DIVIDEND_PAYABLE", "CT");
        h.Clock.UtcNow = new DateTime(2026, 6, 30, 8, 0, 0, DateTimeKind.Utc);
        var eq = new EquityService(s.Db, s.Engine, s.Audit, h.Clock);
        var a = (await s.Setup.CreatePartnerAsync(h.Admin, "CD1", "Cổ đông cá nhân A", PartnerRoles.Other)).Id;
        var b = (await s.Setup.CreatePartnerAsync(h.Admin, "CD2", "Công ty cổ đông B", PartnerRoles.Other)).Id;
        var c = (await s.Setup.CreatePartnerAsync(h.Admin, "CD3", "Cổ đông cá nhân C", PartnerRoles.Other)).Id;
        var sa = await eq.AddShareholderAsync(h.Admin, new(a, 600_000_000, true));
        var sb = await eq.AddShareholderAsync(h.Admin, new(b, 300_000_000, false));
        var sc = await eq.AddShareholderAsync(h.Admin, new(c, 100_000_000, true));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => eq.AddShareholderAsync(h.Admin, new(a, 1, true))));

        // Chưa góp vốn thì chưa chia được; góp vốn tạo chứng từ nháp, chỉ tính khi chứng từ còn hiệu lực.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => eq.CreateDividendAsync(h.Preparer, new(new(2026, 6, 30), "NQ 01", 1_000_000, 5))));
        var v1 = await eq.RecordMovementAsync(h.Preparer, new(sa.Id, CapitalMovementKind.Contribution, new(2026, 1, 5), 600_000_000, h.Acc["CASH"]));
        var v2 = await eq.RecordMovementAsync(h.Preparer, new(sb.Id, CapitalMovementKind.Contribution, new(2026, 1, 5), 300_000_000, h.Acc["CASH"]));
        var v3 = await eq.RecordMovementAsync(h.Preparer, new(sc.Id, CapitalMovementKind.Contribution, new(2026, 1, 5), 100_000_000, h.Acc["CASH"]));
        foreach (var v in new[] { v1, v2, v3 }) await PostAsync(h, s, v.Id);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => eq.RecordMovementAsync(h.Preparer, new(sc.Id, CapitalMovementKind.Withdrawal, new(2026, 2, 1), 150_000_000, h.Acc["CASH"]))));   // rút vượt số thực góp
        var reg = (await eq.ShareholdersAsync(h.Auditor, new(2026, 6, 30))).ToDictionary(r => r.PartnerCode);
        Assert.Equal((600_000_000m, 60m, 0m), (reg["CD1"].Contributed, reg["CD1"].OwnershipPercent!.Value, reg["CD1"].Remaining));
        Assert.Equal(1_000_000_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CASH"], new(2026, 6, 30)));

        // Phương án: chia 100.000.001 đồng theo 60/30/10 — phần dư làm tròn dồn vào người góp nhiều nhất; TNCN 5% chỉ cho cá nhân.
        var d = await eq.CreateDividendAsync(h.Preparer, new(new(2026, 6, 30), "NQ ĐHĐCĐ số 01, LNST năm 2025", 100_000_001, 5));
        var view = await eq.DividendAsync(h.Auditor, d.Id);
        Assert.Equal(100_000_001m, view.Lines.Sum(l => l.Gross));
        var la = view.Lines.Single(l => l.PartnerCode == "CD1"); var lb = view.Lines.Single(l => l.PartnerCode == "CD2"); var lc = view.Lines.Single(l => l.PartnerCode == "CD3");
        Assert.Equal((60_000_001m, 3_000_000m, 57_000_001m), (la.Gross, la.PitWithheld, la.Net));   // 60.000.000,6 làm tròn 60.000.001; TNCN 5% = 3.000.000,05 → 3.000.000
        Assert.Equal((0m, lb.Gross), (lb.PitWithheld, lb.Net));
        Assert.Equal((10_000_000m, 500_000m, 9_500_000m), (lc.Gross, lc.PitWithheld, lc.Net));
        // Duyệt: người lập không tự duyệt; chưa duyệt thì chưa ghi nhận.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => eq.DeclareAsync(h.Preparer, d.Id, new(2026, 6, 30))));
        await eq.DecideDividendAsync(h.Approver, d.Id, true, null);
        // Có thuế phải nộp mà chưa ánh xạ vai trò TNCN → từ chối, không đoán TK.
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => eq.DeclareAsync(h.Preparer, d.Id, new(2026, 6, 30))));
        await s.Rules.SetRoleMappingAsync(h.Admin, "DIVIDEND_PIT_PAYABLE", "PIT");
        var decl = await eq.DeclareAsync(h.Preparer, d.Id, new(2026, 6, 30));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => eq.DeclareAsync(h.Preparer, d.Id, new(2026, 6, 30))));
        // Chi cổ tức chỉ sau khi chứng từ ghi nhận đã ghi sổ.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => eq.CreatePaymentAsync(h.Preparer, d.Id, sa.Id, h.Acc["CASH"], new(2026, 7, 1))));
        await PostAsync(h, s, decl.Id);
        Assert.Equal(100_000_001m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["LNST"], new(2026, 6, 30)));
        Assert.Equal(-3_500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["PIT"], new(2026, 6, 30)));
        Assert.Equal(-96_500_001m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CT"], new(2026, 6, 30)));
        var pay = await eq.CreatePaymentAsync(h.Preparer, d.Id, sa.Id, h.Acc["CASH"], new(2026, 7, 1));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => eq.CreatePaymentAsync(h.Preparer, d.Id, sa.Id, h.Acc["CASH"], new(2026, 7, 1))));
        await PostAsync(h, s, pay.Id);
        Assert.Equal(0m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CT"], new(2026, 7, 31), a));
        var after = await eq.DividendAsync(h.Auditor, d.Id);
        Assert.True(after.Lines.Single(l => l.PartnerCode == "CD1").Paid);
        Assert.False(after.Lines.Single(l => l.PartnerCode == "CD2").Paid);
        Assert.Equal(DividendStatus.Declared, after.Row.Status);
        // Đã ghi nhận thì không hủy phương án được.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => eq.CancelDividendAsync(h.Preparer, d.Id, "x")));
    }
}
