using Hak.Domain.Catalog;
using Hak.Domain.Common;

namespace Hak.Domain.Vouchers;

/// <summary>
/// Pure validation of a voucher against reference data. No I/O, so the same rules run
/// in ValidateVoucher (preview) and again inside the posting transaction.
/// </summary>
public static class VoucherValidator
{
    /// <param name="currencyDecimals">
    /// Decimal places per currency code. When given, a foreign line must use a known currency and its foreign amount may not have more
    /// decimals than that currency allows (ACC-08: JPY 1.5 or an unknown code used to pass).
    /// </param>
    public static List<ValidationError> Validate(
        Voucher voucher,
        IReadOnlyDictionary<Guid, Account> accounts,
        IReadOnlyDictionary<Guid, BusinessPartner> partners,
        int functionalDecimals,
        IReadOnlyDictionary<string, int>? currencyDecimals = null)
    {
        var errors = new List<ValidationError>();
        if (voucher.Lines.Count == 0)
        {
            errors.Add(new(ErrorCodes.VoucherNoLines, "Chứng từ chưa có dòng định khoản."));
            return errors;
        }

        foreach (var line in voucher.Lines)
        {
            if (line.Amount <= 0)
                errors.Add(new(ErrorCodes.AmountNotPositive, "Số tiền phải lớn hơn 0.", line.LineNo));
            else if (!MoneyMath.HasAtMostDecimals(line.Amount, functionalDecimals))
                errors.Add(new(ErrorCodes.AmountPrecision,
                    $"Số tiền {line.Amount} vượt quá {functionalDecimals} chữ số thập phân của đồng tiền hạch toán.", line.LineNo));

            if (line.Side is not (Side.Debit or Side.Credit))
                errors.Add(new(ErrorCodes.ValidationFailed, "Chiều Nợ/Có không hợp lệ.", line.LineNo));

            if (!accounts.TryGetValue(line.AccountId, out var account))
            {
                errors.Add(new(ErrorCodes.AccountNotFound, "Tài khoản không tồn tại.", line.LineNo));
            }
            else
            {
                if (account.CompanyId != voucher.CompanyId)
                    errors.Add(new(ErrorCodes.AccountWrongCompany, $"Tài khoản {account.Code} không thuộc công ty của chứng từ.", line.LineNo));
                if (!account.IsPostable)
                    errors.Add(new(ErrorCodes.AccountNotPostable, $"Tài khoản {account.Code} là tài khoản tổng hợp, không hạch toán trực tiếp.", line.LineNo));
                if (!account.IsActive)
                    errors.Add(new(ErrorCodes.AccountInactive, $"Tài khoản {account.Code} đã ngừng sử dụng.", line.LineNo));
                if (account.RequiresPartner && line.PartnerId is null)
                    errors.Add(new(ErrorCodes.PartnerRequired, $"Tài khoản {account.Code} yêu cầu đối tượng chi tiết.", line.LineNo));
            }

            if (line.PartnerId is { } pid)
            {
                if (!partners.TryGetValue(pid, out var partner) || partner.CompanyId != voucher.CompanyId)
                    errors.Add(new(ErrorCodes.PartnerWrongCompany, "Đối tượng không tồn tại hoặc không thuộc công ty.", line.LineNo));
            }

            // A revaluation line adjusts only the functional amount of a currency's balance: it carries the currency tag
            // without a foreign amount or rate. Allowed only on revaluation vouchers and on reversals (which mirror them).
            var revaluationTag = voucher.VoucherType is VoucherTypes.Revaluation or VoucherTypes.Reversal
                                 && !string.IsNullOrEmpty(line.ForeignCurrency) && line.ForeignAmount is null && line.ExchangeRate is null;
            if (!revaluationTag && (line.ForeignAmount is not null || line.ExchangeRate is not null || line.ForeignCurrency is not null))
            {
                if (line.ForeignAmount is null || line.ExchangeRate is null || string.IsNullOrEmpty(line.ForeignCurrency))
                    errors.Add(new(ErrorCodes.ValidationFailed, "Dòng ngoại tệ phải có đủ mã nguyên tệ, số nguyên tệ và tỷ giá.", line.LineNo));
                else if (line.ExchangeRate <= 0 || line.ForeignAmount <= 0)
                    errors.Add(new(ErrorCodes.AmountNotPositive, "Nguyên tệ và tỷ giá phải lớn hơn 0.", line.LineNo));
                else if (currencyDecimals is not null && !currencyDecimals.TryGetValue(line.ForeignCurrency, out _))
                    errors.Add(new(ErrorCodes.ValidationFailed, $"Loại tiền {line.ForeignCurrency} không có trong danh mục loại tiền.", line.LineNo));
                else if (currencyDecimals is not null && !MoneyMath.HasAtMostDecimals(line.ForeignAmount.Value, currencyDecimals[line.ForeignCurrency]))
                    errors.Add(new(ErrorCodes.AmountPrecision,
                        $"Số nguyên tệ {line.ForeignAmount} vượt quá {currencyDecimals[line.ForeignCurrency]} chữ số thập phân của {line.ForeignCurrency}.", line.LineNo));
                else if (MoneyMath.Convert(line.ForeignAmount.Value, line.ExchangeRate.Value, functionalDecimals) != line.Amount)
                    errors.Add(new(ErrorCodes.ValidationFailed,
                        $"Số tiền quy đổi {line.Amount} khác nguyên tệ × tỷ giá đã làm tròn " +
                        $"({MoneyMath.Convert(line.ForeignAmount.Value, line.ExchangeRate.Value, functionalDecimals)}).", line.LineNo));
            }
        }

        if (voucher.Lines.All(l => l.Side == Side.Debit) || voucher.Lines.All(l => l.Side == Side.Credit))
            errors.Add(new(ErrorCodes.VoucherUnbalanced, "Chứng từ phải có cả dòng Nợ và dòng Có."));
        else if (voucher.TotalDebit != voucher.TotalCredit)
            errors.Add(new(ErrorCodes.VoucherUnbalanced,
                $"Tổng Nợ ({voucher.TotalDebit}) khác tổng Có ({voucher.TotalCredit})."));

        return errors;
    }
}
