using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Banking;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Banking;

public sealed record BankAccountInput(string BankName, string? Branch, string AccountNo, string AccountName, bool IsDefault = false);
public sealed record CompanyBankAccountInput(Guid AccountId, string BankName, string? Branch, string AccountNo, string AccountName);
public sealed record PartnerBankAccountView(Guid Id, Guid PartnerId, string PartnerCode, string PartnerName, string BankName, string? Branch, string AccountNo, string AccountName, bool IsDefault, bool IsActive);
public sealed record CompanyBankAccountView(Guid Id, Guid AccountId, string AccountCode, string BankName, string? Branch, string AccountNo, string AccountName, bool IsActive);

/// <summary>
/// Số tài khoản ngân hàng: của đối tượng nhận tiền (một mặc định) và của chính công ty (gắn với TK tiền gửi sổ cái). Số tài khoản chỉ gồm chữ và số;
/// lệnh thanh toán không nhập tay thông tin thụ hưởng thì lấy tài khoản mặc định của đối tượng (chụp lại lúc lập lệnh, đổi sau không làm đổi lệnh cũ).
/// </summary>
public sealed class BankAccountService(HakDbContext db, AuditService audit, IClock clock)
{
    private static string Clean(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());

    private static void Validate(string bankName, string accountNo, string accountName)
    {
        if (string.IsNullOrWhiteSpace(bankName)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên ngân hàng bắt buộc.");
        if (string.IsNullOrWhiteSpace(accountName)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên chủ tài khoản bắt buộc.");
        var no = Clean(accountNo);
        if (no.Length is < 6 or > 24) throw new DomainException(ErrorCodes.ValidationFailed, "Số tài khoản phải từ 6 đến 24 ký tự chữ hoặc số.");
    }

    public async Task<IReadOnlyList<PartnerBankAccountView>> PartnerAccountsAsync(SessionContext ctx, Guid? partnerId = null)
    {
        ctx.Require(Permissions.VoucherView);
        var q = from a in db.PartnerBankAccounts join p in db.BusinessPartners on a.PartnerId equals p.Id
                where a.CompanyId == ctx.CompanyId && (partnerId == null || a.PartnerId == partnerId)
                select new PartnerBankAccountView(a.Id, p.Id, p.Code, p.Name, a.BankName, a.Branch, a.AccountNo, a.AccountName, a.IsDefault, a.IsActive);
        return (await q.AsNoTracking().ToListAsync()).OrderBy(r => r.PartnerCode, StringComparer.Ordinal).ThenByDescending(r => r.IsDefault).ThenBy(r => r.BankName, StringComparer.Ordinal).ToList();
    }

    public async Task<PartnerBankAccount> AddPartnerAccountAsync(SessionContext ctx, Guid partnerId, BankAccountInput input)
    {
        ctx.Require(Permissions.CatalogManage);
        Validate(input.BankName, input.AccountNo, input.AccountName);
        return await db.InTransactionAsync(async () =>
        {
            if (!await db.BusinessPartners.AnyAsync(p => p.Id == partnerId && p.CompanyId == ctx.CompanyId)) throw new DomainException(ErrorCodes.PartnerWrongCompany, "Đối tượng không tồn tại.");
            var no = Clean(input.AccountNo); var bank = input.BankName.Trim();
            if (await db.PartnerBankAccounts.AnyAsync(a => a.CompanyId == ctx.CompanyId && a.PartnerId == partnerId && a.AccountNo == no && a.BankName == bank))
                throw new DomainException(ErrorCodes.DuplicateCode, $"Tài khoản {no} tại {bank} đã có.");
            var mine = await db.PartnerBankAccounts.Where(a => a.CompanyId == ctx.CompanyId && a.PartnerId == partnerId && a.IsActive).ToListAsync();
            var makeDefault = input.IsDefault || mine.Count == 0;
            if (makeDefault) foreach (var m in mine) m.IsDefault = false;
            var acc = new PartnerBankAccount { Id = Ids.New(), CompanyId = ctx.CompanyId, PartnerId = partnerId, BankName = bank, Branch = input.Branch?.Trim(), AccountNo = no,
                AccountName = input.AccountName.Trim(), IsDefault = makeDefault, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.PartnerBankAccounts.Add(acc);
            await audit.AppendAsync(ctx.CompanyId, ctx, "bank_account.partner_add", "partner_bank_account", acc.Id.ToString(), new { partnerId, bank, no, acc.AccountName, makeDefault });
            await db.SaveChangesAsync();
            return acc;
        });
    }

    public async Task SetPartnerAccountAsync(SessionContext ctx, Guid id, bool? isDefault, bool? isActive)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            var a = await db.PartnerBankAccounts.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Tài khoản ngân hàng không tồn tại.");
            if (isActive == false) { a.IsActive = false; a.IsDefault = false; }
            else if (isActive == true) a.IsActive = true;
            if (isDefault == true)
            {
                if (!a.IsActive) throw new DomainException(ErrorCodes.ValidationFailed, "Tài khoản đã ngừng dùng không đặt làm mặc định.");
                foreach (var o in await db.PartnerBankAccounts.Where(x => x.CompanyId == ctx.CompanyId && x.PartnerId == a.PartnerId && x.Id != id).ToListAsync()) o.IsDefault = false;
                a.IsDefault = true;
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "bank_account.partner_set", "partner_bank_account", a.Id.ToString(), new { isDefault, isActive });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Thông tin thụ hưởng mặc định của đối tượng (tài khoản mặc định còn dùng), null nếu chưa khai báo.</summary>
    public async Task<PartnerBankAccount?> DefaultAccountAsync(Guid companyId, Guid partnerId)
        => await db.PartnerBankAccounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.PartnerId == partnerId && a.IsActive)
                   .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<CompanyBankAccountView>> CompanyAccountsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.VoucherView);
        var q = from a in db.CompanyBankAccounts join g in db.Accounts on a.AccountId equals g.Id where a.CompanyId == ctx.CompanyId
                select new CompanyBankAccountView(a.Id, a.AccountId, g.Code, a.BankName, a.Branch, a.AccountNo, a.AccountName, a.IsActive);
        return (await q.AsNoTracking().ToListAsync()).OrderBy(r => r.AccountCode, StringComparer.Ordinal).ThenBy(r => r.AccountNo, StringComparer.Ordinal).ToList();
    }

    public async Task<CompanyBankAccount> AddCompanyAccountAsync(SessionContext ctx, CompanyBankAccountInput input)
    {
        ctx.Require(Permissions.CatalogManage);
        Validate(input.BankName, input.AccountNo, input.AccountName);
        return await db.InTransactionAsync(async () =>
        {
            var gl = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.AccountId && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản sổ cái không tồn tại.");
            if (gl.CashKind != CashKind.Bank) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {gl.Code} không phải tài khoản tiền gửi ngân hàng.");
            var no = Clean(input.AccountNo); var bank = input.BankName.Trim();
            if (await db.CompanyBankAccounts.AnyAsync(a => a.CompanyId == ctx.CompanyId && a.AccountNo == no && a.BankName == bank)) throw new DomainException(ErrorCodes.DuplicateCode, $"Tài khoản {no} tại {bank} đã có.");
            var acc = new CompanyBankAccount { Id = Ids.New(), CompanyId = ctx.CompanyId, AccountId = gl.Id, BankName = bank, Branch = input.Branch?.Trim(), AccountNo = no, AccountName = input.AccountName.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.CompanyBankAccounts.Add(acc);
            await audit.AppendAsync(ctx.CompanyId, ctx, "bank_account.company_add", "company_bank_account", acc.Id.ToString(), new { gl = gl.Code, bank, no });
            await db.SaveChangesAsync();
            return acc;
        });
    }

    public async Task SetCompanyAccountActiveAsync(SessionContext ctx, Guid id, bool active)
    {
        ctx.Require(Permissions.CatalogManage);
        await db.InTransactionAsync(async () =>
        {
            var a = await db.CompanyBankAccounts.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Tài khoản ngân hàng không tồn tại.");
            a.IsActive = active;
            await audit.AppendAsync(ctx.CompanyId, ctx, "bank_account.company_set", "company_bank_account", a.Id.ToString(), new { active });
            await db.SaveChangesAsync();
        });
    }
}
