using System.Text;
using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Banking;

/// <summary>Suggested counter account for a statement line (by its text and direction), never applied without the user.</summary>
public sealed record StatementLineSuggestion(Guid StatementLineId, string? RoleCode, Guid? AccountId, string? AccountCode, string Reason);

/// <summary>
/// Bank-only transactions (fees, interest, transfers not yet booked): creates a DRAFT voucher from an unmatched statement line —
/// bank account on one side, the counter account the user chose on the other, dated on the transaction date. It goes through the
/// normal submit → approve → post; auto-matching then pairs it with the statement line. Fees / interest are only suggested from
/// the line text via the optional roles BANK_FEE / BANK_INTEREST_INCOME.
/// </summary>
public sealed class StatementVoucherService(HakDbContext db, AccountingEngine engine, AuditService audit)
{
    public const string Source = "HAK.BANKSTMT";

    private static readonly string[] FeeWords = ["phi", "fee", "charge", "thu phi", "sms banking", "quan ly tai khoan"];
    private static readonly string[] InterestWords = ["lai", "interest", "tra lai", "lai tien gui", "lai nhap von"];

    public async Task<StatementLineSuggestion> SuggestAsync(SessionContext ctx, Guid statementLineId)
    {
        ctx.Require(Permissions.BankReconcile);
        var sl = await LineAsync(ctx, statementLineId);
        var text = " " + Plain(sl.Description) + " ";
        static bool Has(string text, string[] words) => words.Any(w => text.Contains(" " + w + " ", StringComparison.Ordinal));
        var (role, reason) = sl.Amount < 0 && Has(text, FeeWords) ? ("BANK_FEE", "Nội dung có dấu hiệu phí ngân hàng (tiền ra).")
            : sl.Amount > 0 && Has(text, InterestWords) ? ("BANK_INTEREST_INCOME", "Nội dung có dấu hiệu lãi tiền gửi (tiền vào).")
            : ((string?)null, "Không nhận ra loại giao dịch — chọn TK đối ứng.");
        if (role is null) return new(sl.Id, null, null, null, reason);
        var mapped = await (from m in db.AccountRoleMappings join a in db.Accounts on m.AccountId equals a.Id
                            where m.CompanyId == ctx.CompanyId && m.RoleCode == role select new { a.Id, a.Code }).FirstOrDefaultAsync();
        return mapped is null
            ? new(sl.Id, role, null, null, reason + $" Vai trò {role} chưa ánh xạ TK (Thiết lập → Vai trò tài khoản).")
            : new(sl.Id, role, mapped.Id, mapped.Code, reason);
    }

    public async Task<Voucher> CreateDraftAsync(SessionContext ctx, Guid statementLineId, Guid counterAccountId, Guid? partnerId, string? description)
    {
        ctx.Require(Permissions.BankReconcile);
        ctx.Require(Permissions.VoucherCreate);
        // ACC-07 / DB-10: kiểm trùng, tạo nháp và ghi nhật ký trong MỘT giao dịch (một người ghi tại một thời điểm): trước đây nhật ký ghi
        // ở lần lưu riêng sau khi nháp đã lưu — lỗi ở giữa để lại chứng từ không có nhật ký, hai người bấm cùng lúc tạo được hai nháp.
        return await db.InTransactionAsync(() => CreateDraftCoreAsync(ctx, statementLineId, counterAccountId, partnerId, description));
    }

    private async Task<Voucher> CreateDraftCoreAsync(SessionContext ctx, Guid statementLineId, Guid counterAccountId, Guid? partnerId, string? description)
    {
        var sl = await LineAsync(ctx, statementLineId);
        if (sl.Status == StatementLineStatus.Matched) throw new DomainException(ErrorCodes.MatchInvalid, "Dòng sao kê đã được đối chiếu.");
        if (counterAccountId == sl.BankAccountId) throw new DomainException(ErrorCodes.ValidationFailed, "TK đối ứng phải khác TK ngân hàng.");
        var open = await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == Source && v.SourceId!.StartsWith(sl.Id.ToString()))
            .Select(v => new { v.Number, v.Status }).ToListAsync();
        if (open.FirstOrDefault(v => v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed) is { } existing)
            throw new DomainException(ErrorCodes.DuplicateSource, $"Dòng sao kê đã có chứng từ {existing.Number}; xử lý chứng từ đó (hoặc hủy) trước.");
        var amount = Math.Abs(sl.Amount);
        var text = string.IsNullOrWhiteSpace(description) ? (BankReconciliationService.IsAutoReference(sl.BankReference) ? sl.Description : $"{sl.Description} (GD {sl.BankReference})").Trim() : description.Trim();
        VoucherLineInput bank = new(sl.BankAccountId, sl.Amount > 0 ? Side.Debit : Side.Credit, amount, Description: text);
        VoucherLineInput counter = new(counterAccountId, sl.Amount > 0 ? Side.Credit : Side.Debit, amount, partnerId, Description: text);
        var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, sl.TransactionDate, sl.TransactionDate, text,
            sl.Amount > 0 ? [bank, counter] : [counter, bank], SourceSystem: Source, SourceId: $"{sl.Id}:{open.Count + 1}"));
        await audit.AppendAsync(ctx.CompanyId, ctx, "bank_statement.voucher_draft", "bank_statement_line", sl.Id.ToString(), new { v.Number, counterAccountId, amount });
        await db.SaveChangesAsync();
        return v;
    }

    private async Task<BankStatementLine> LineAsync(SessionContext ctx, Guid id) =>
        await db.BankStatementLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == ctx.CompanyId)
        ?? throw new DomainException(ErrorCodes.NotFound, "Dòng sao kê không tồn tại.");

    /// <summary>Lower-case, no diacritics, punctuation as spaces: "Phí SMS-Banking" → "phi sms banking".</summary>
    internal static string Plain(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            var x = char.ToLowerInvariant(c);
            x = Fold.TryGetValue(x, out var b) ? b : x;
            sb.Append(char.IsLetterOrDigit(x) ? x : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // The server runs with invariant globalization (no Unicode decomposition), so Vietnamese letters are folded by table.
    private static readonly Dictionary<char, char> Fold = new[]
    {
        ("àáạảãâầấậẩẫăằắặẳẵ", 'a'), ("èéẹẻẽêềếệểễ", 'e'), ("ìíịỉĩ", 'i'), ("òóọỏõôồốộổỗơờớợởỡ", 'o'),
        ("ùúụủũưừứựửữ", 'u'), ("ỳýỵỷỹ", 'y'), ("đ", 'd'),
    }.SelectMany(p => p.Item1.Select(c => (c, p.Item2))).ToDictionary(p => p.c, p => p.Item2);
}
