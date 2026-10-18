using Hak.Application.Accounting;
using Hak.Application.Setup;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Opening detail accounts under a TT99 account (cây TK cha – con) and changing account attributes.</summary>
public class AccountTreeTests
{
    private static async Task<string> MessageOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Message;

    [Fact]
    public async Task ChildAccount_SplitsAnUnusedParent_InheritsAttributes_AndRefusesUsedParents()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var csv = File.ReadAllLines(Path.Combine(ReportEngineTests.LegalDir(), "nhap-he-thong-tk-de-xuat.csv")).Skip(1).Select(l => l.Split(';'))
            .Select((c, i) => new AccountImportRow(i + 2, c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8])).ToList();
        Assert.True((await s.CatalogImport.ImportAccountsAsync(h.Admin, csv, commit: true)).Committed);
        async Task<Account> Acc(string code) => await s.Db.Accounts.AsNoTracking().SingleAsync(a => a.Code == code);

        // 131 (detail, theo dõi đối tượng, phải thu) → 1311 inherits; 131 becomes a summary account.
        await s.Setup.SetAccountTagsAsync(h.Admin, (await Acc("131")).Id, "DH");
        var c1311 = await s.Setup.AddChildAccountAsync(h.Admin, (await Acc("131")).Id, "1311", "Phải thu khách hàng trong nước");
        Assert.False((await Acc("131")).IsPostable);
        Assert.Equal((true, OpenItemNature.Receivable, BalanceDisplay.GrossByPartner, "DH", (Guid?)(await Acc("131")).Id),
            (c1311.RequiresPartner, c1311.OpenItemNature, c1311.BalanceDisplay, c1311.ReportTags, c1311.ParentId));
        // A second child under the now-summary parent, with its own attributes.
        var c1312 = await s.Setup.AddChildAccountAsync(h.Admin, (await Acc("131")).Id, "1312", "Phải thu khách hàng nước ngoài",
            new AccountAttributes(NegativeBalancePolicy: NegativeBalancePolicy.Warn, ReportTags: ""));
        Assert.Equal((NegativeBalancePolicy.Warn, (string?)null), (c1312.NegativeBalancePolicy, c1312.ReportTags));
        Assert.Contains("bắt đầu bằng 131", await MessageOf(async () => await s.Setup.AddChildAccountAsync(h.Admin, (await Acc("131")).Id, "1411", "x")));

        // A parent with postings or a role mapping stays a detail account.
        await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 1, 5), new(2026, 1, 5), "Góp vốn",
            [new VoucherLineInput((await Acc("112")).Id, Side.Debit, 100), new VoucherLineInput((await Acc("41111")).Id, Side.Credit, 100)]));
        Assert.Contains("đã có bút toán", await MessageOf(async () => await s.Setup.AddChildAccountAsync(h.Admin, (await Acc("112")).Id, "1121", "Ngân hàng A")));
        await s.Rules.SetRoleMappingAsync(h.Admin, "BANK_FEE", "6427");
        Assert.Contains("BANK_FEE", await MessageOf(async () => await s.Setup.AddChildAccountAsync(h.Admin, (await Acc("6427")).Id, "64271", "Phí ngân hàng")));
        Assert.True((await Acc("112")).IsPostable);

        // Attributes: structural ones only while unused; the negative-balance policy at any time.
        await s.Setup.UpdateAccountAttributesAsync(h.Admin, c1312.Id, true, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.Block);
        Assert.Equal(OpenItemNature.None, (await Acc("1312")).OpenItemNature);
        Assert.Contains("chỉ đổi được chính sách số dư âm", await MessageOf(async () => await            s.Setup.UpdateAccountAttributesAsync(h.Admin, (await Acc("112")).Id, false, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.Block)));
        await s.Setup.UpdateAccountAttributesAsync(h.Admin, (await Acc("112")).Id, false, OpenItemNature.None, CashKind.Bank, NegativeBalancePolicy.Block);
        Assert.Equal(NegativeBalancePolicy.Block, (await Acc("112")).NegativeBalancePolicy);
        Assert.Equal(ErrorCodes.ValidationFailed, (await Assert.ThrowsAsync<DomainException>(async () => await
            s.Setup.UpdateAccountAttributesAsync(h.Admin, c1311.Id, false, OpenItemNature.Receivable, CashKind.None, NegativeBalancePolicy.None))).Code);
    }
}
