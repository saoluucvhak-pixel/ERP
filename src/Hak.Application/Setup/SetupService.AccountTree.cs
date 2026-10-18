using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

/// <summary>Attributes of a detail account; null on a child = inherit from the parent being split.</summary>
public sealed record AccountAttributes(bool? RequiresPartner = null, OpenItemNature? OpenItemNature = null, CashKind? CashKind = null,
    NegativeBalancePolicy? NegativeBalancePolicy = null, string? ReportTags = null);

public sealed partial class SetupService
{
    /// <summary>
    /// Opens a detail account under <paramref name="parentId"/> (Điều 11 khoản 2: doanh nghiệp mở thêm TK chi tiết). The child's code
    /// starts with the parent's code so prefix formulas (131*) keep working. When the parent is still a detail account it becomes a
    /// summary account — allowed only while nothing uses it yet (no postings, vouchers, role mappings, module links) — and the child
    /// takes over its attributes (đối tượng, công nợ, loại tiền, số dư âm, nhãn BC) unless given others.
    /// </summary>
    public async Task<Account> AddChildAccountAsync(SessionContext ctx, Guid parentId, string code, string name, AccountAttributes? attributes = null)
    {
        ctx.Require(Permissions.CatalogManage);
        code = (code ?? "").Trim().ToUpperInvariant();
        RequireCode(code);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên tài khoản bắt buộc.");
        return await db.InTransactionAsync(async () =>
        {
            var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Id == parentId && a.CompanyId == ctx.CompanyId)
                         ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản cấp trên không tồn tại.");
            if (!parent.IsActive) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {parent.Code} đã ngừng sử dụng.");
            if (code.Length <= parent.Code.Length || !code.StartsWith(parent.Code, StringComparison.Ordinal))
                throw new DomainException(ErrorCodes.ValidationFailed, $"Mã TK chi tiết phải bắt đầu bằng {parent.Code} và dài hơn (vd {parent.Code}1).");
            if (await db.Accounts.AnyAsync(a => a.CompanyId == ctx.CompanyId && a.Code == code))
                throw new DomainException(ErrorCodes.DuplicateCode, $"Tài khoản {code} đã tồn tại.");
            var split = parent.IsPostable;
            if (split)
            {
                var uses = await UsesAsync(ctx.CompanyId, parent.Id);
                if (uses.Count > 0)
                    throw new DomainException(ErrorCodes.ValidationFailed,
                        $"TK {parent.Code} đang được dùng ({string.Join("; ", uses)}) nên không chuyển thành TK tổng hợp được. "
                        + "Mở TK chi tiết trước khi hạch toán, hoặc mở TK mới cùng cấp.");
            }
            var a = attributes ?? new AccountAttributes();
            var requiresPartner = a.RequiresPartner ?? parent.RequiresPartner;
            var nature = a.OpenItemNature ?? parent.OpenItemNature;
            var cash = a.CashKind ?? parent.CashKind;
            ValidateAttributes(requiresPartner, nature, cash);
            var child = new Account
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Code = code, Name = name.Trim(), ParentId = parent.Id, IsPostable = true,
                RequiresPartner = requiresPartner, OpenItemNature = nature, CashKind = cash,
                BalanceDisplay = nature == OpenItemNature.None ? parent.BalanceDisplay : BalanceDisplay.GrossByPartner,
                NegativeBalancePolicy = a.NegativeBalancePolicy ?? parent.NegativeBalancePolicy,
                ReportTags = a.ReportTags is null ? parent.ReportTags : NormalizeTags(a.ReportTags),
                CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.Accounts.Add(child);
            if (split)
            {
                parent.IsPostable = false;
                parent.RowVersion++;
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "account.create", "account", child.Id.ToString(),
                new { code, name, parentCode = parent.Code, parentBecameSummary = split, child.RequiresPartner, child.OpenItemNature, child.CashKind, child.NegativeBalancePolicy, child.ReportTags });
            await db.SaveChangesAsync();
            return child;
        });
    }

    /// <summary>
    /// Changes the management attributes of a detail account. The negative-balance policy can change at any time; partner
    /// tracking, open-item nature and cash kind only while the account has no postings or vouchers (they decide how lines are
    /// validated and settled, so changing them later would leave earlier lines inconsistent).
    /// </summary>
    public async Task UpdateAccountAttributesAsync(SessionContext ctx, Guid accountId, bool requiresPartner, OpenItemNature openItemNature,
        CashKind cashKind, NegativeBalancePolicy negativeBalancePolicy)
    {
        ctx.Require(Permissions.CatalogManage);
        ValidateAttributes(requiresPartner, openItemNature, cashKind);
        await db.InTransactionAsync(async () =>
        {
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản không tồn tại.");
            var before = new { acc.RequiresPartner, acc.OpenItemNature, acc.CashKind, acc.NegativeBalancePolicy };
            var structural = acc.RequiresPartner != requiresPartner || acc.OpenItemNature != openItemNature || acc.CashKind != cashKind;
            if (structural)
            {
                var used = await db.JournalLines.AnyAsync(l => l.AccountId == acc.Id) || await db.VoucherLines.AnyAsync(l => l.AccountId == acc.Id);
                if (used) throw new DomainException(ErrorCodes.ValidationFailed,
                    $"TK {acc.Code} đã có chứng từ: chỉ đổi được chính sách số dư âm; đối tượng, công nợ, loại tiền giữ nguyên.");
            }
            if (!structural && acc.NegativeBalancePolicy == negativeBalancePolicy) return;
            acc.RequiresPartner = requiresPartner;
            acc.OpenItemNature = openItemNature;
            acc.CashKind = cashKind;
            acc.NegativeBalancePolicy = negativeBalancePolicy;
            if (openItemNature != OpenItemNature.None) acc.BalanceDisplay = BalanceDisplay.GrossByPartner;
            acc.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "account.attributes", "account", acc.Id.ToString(),
                new { acc.Code, before, after = new { requiresPartner, openItemNature, cashKind, negativeBalancePolicy } }, acc.RowVersion);
            await db.SaveChangesAsync();
        });
    }

    private static void ValidateAttributes(bool requiresPartner, OpenItemNature nature, CashKind cash)
    {
        if (nature != OpenItemNature.None && !requiresPartner)
            throw new DomainException(ErrorCodes.ValidationFailed, "Tài khoản theo dõi công nợ theo chứng từ phải bắt buộc đối tượng.");
        if (nature != OpenItemNature.None && cash != CashKind.None)
            throw new DomainException(ErrorCodes.ValidationFailed, "Tài khoản tiền không thể đồng thời là tài khoản công nợ.");
    }

    /// <summary>Where an account is used — any of these blocks turning it into a summary account.</summary>
    private async Task<List<string>> UsesAsync(Guid companyId, Guid id)
    {
        var uses = new List<string>();
        async Task Check(string what, Task<bool> any) { if (await any) uses.Add(what); }
        await Check("đã có bút toán", db.JournalLines.AnyAsync(l => l.AccountId == id));
        await Check("có trên chứng từ", db.VoucherLines.AnyAsync(l => l.AccountId == id));
        var roles = await db.AccountRoleMappings.Where(m => m.CompanyId == companyId && m.AccountId == id).Select(m => m.RoleCode).ToListAsync();
        if (roles.Count > 0) uses.Add("ánh xạ vai trò " + string.Join(", ", roles));
        await Check("TK kho của vật tư", db.Items.AnyAsync(i => i.InventoryAccountId == id));
        await Check("thẻ tài sản", db.LongTermAssets.AnyAsync(a => a.AssetAccountId == id || a.AccumulatedAccountId == id || a.ExpenseAccountId == id));
        await Check("khế ước vay", db.LoanContracts.AnyAsync(l => l.PrincipalAccountId == id || l.InterestExpenseAccountId == id || l.AccruedInterestAccountId == id));
        await Check("đối tượng tính giá thành", db.CostObjects.AnyAsync(c => c.WipAccountId == id));
        await Check("bước kết chuyển", db.ClosingSteps.AnyAsync(s => s.TargetAccountId == id));
        await Check("ủy nhiệm chi / sao kê", db.PaymentOrders.AnyAsync(p => p.BankAccountId == id));
        await Check("sao kê ngân hàng", db.BankStatements.AnyAsync(b => b.BankAccountId == id));
        return uses;
    }
}
