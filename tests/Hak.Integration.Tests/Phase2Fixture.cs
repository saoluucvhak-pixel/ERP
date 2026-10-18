using Hak.Application.Rules;
using Hak.Domain.Rules;
using Hak.Domain.Vouchers;

namespace Hak.Integration.Tests;

/// <summary>Standard test configuration for phase 2: role mappings and posting rules with their own test cases.</summary>
public static class Phase2Fixture
{
    public const string CustomerReceiptDef = """
        {"lines":[
          {"side":"D","account":"DOC:MAIN_ACCOUNT|ROLE:CASH_DEFAULT"},
          {"side":"C","account":"ROLE:AR_TRADE","partner":"DOC:PARTNER","settle":"LINE:SETTLE"}]}
        """;

    public const string CustomerReceiptTests = """
        [{"name":"thu nợ 1.000.000","document":{"partner_code":"KH01","main_account_code":"CASH","lines":[{"amount":"1000000"}]},
          "expected":[{"side":"D","account_code":"CASH","amount":"1000000"},{"side":"C","account_code":"AR","amount":"1000000","partner_code":"KH01"}]},
         {"name":"không chọn quỹ thì dùng quỹ mặc định","document":{"partner_code":"KH01","lines":[{"amount":"5"}]},
          "expected":[{"side":"D","account_code":"CASH","amount":"5"},{"side":"C","account_code":"AR","amount":"5","partner_code":"KH01"}]}]
        """;

    public const string BankReceiptTests = """
        [{"name":"khách chuyển khoản","document":{"partner_code":"KH01","main_account_code":"BANK","lines":[{"amount":"9"}]},
          "expected":[{"side":"D","account_code":"BANK","amount":"9"},{"side":"C","account_code":"AR","amount":"9","partner_code":"KH01"}]}]
        """;

    public const string SupplierPaymentDef = """
        {"lines":[
          {"side":"D","account":"ROLE:AP_TRADE","partner":"DOC:PARTNER","settle":"LINE:SETTLE"},
          {"side":"C","account":"DOC:MAIN_ACCOUNT"}]}
        """;

    public const string SupplierPaymentTests = """
        [{"name":"trả NCC","document":{"partner_code":"NCC01","main_account_code":"BANK","lines":[{"amount":"700"}]},
          "expected":[{"side":"D","account_code":"AP","amount":"700","partner_code":"NCC01"},{"side":"C","account_code":"BANK","amount":"700"}]}]
        """;

    public const string ExpenseDef = """
        {"lines":[
          {"side":"D","account":"LINE:COUNTER_ACCOUNT","constraint":"NOT_CASH_EQUIVALENT"},
          {"side":"C","account":"DOC:MAIN_ACCOUNT"}]}
        """;

    public const string ExpenseTests = """
        [{"name":"chi phí","document":{"main_account_code":"CASH","lines":[{"amount":"300","counter_account_code":"EXP"}]},
          "expected":[{"side":"D","account_code":"EXP","amount":"300"},{"side":"C","account_code":"CASH","amount":"300"}]},
         {"name":"không được dùng để chuyển tiền","document":{"main_account_code":"CASH","lines":[{"amount":"300","counter_account_code":"BANK"}]},
          "expect_error":"RULE_CONSTRAINT"}]
        """;

    public const string TransferDef = """
        {"lines":[
          {"side":"D","account":"LINE:COUNTER_ACCOUNT","constraint":"CASH_EQUIVALENT"},
          {"side":"C","account":"DOC:MAIN_ACCOUNT"}]}
        """;

    public const string TransferTests = """
        [{"name":"chuyển BANK sang BANK2","document":{"main_account_code":"BANK","lines":[{"amount":"50","counter_account_code":"BANK2"}]},
          "expected":[{"side":"D","account_code":"BANK2","amount":"50"},{"side":"C","account_code":"BANK","amount":"50"}]},
         {"name":"chuyển nội bộ không được vào doanh thu","document":{"main_account_code":"BANK","lines":[{"amount":"50","counter_account_code":"REV"}]},
          "expect_error":"RULE_CONSTRAINT"}]
        """;

    public static async Task ConfigureAsync(TestHost h, Scope s)
    {
        await s.Rules.SetRoleMappingAsync(h.Admin, "AR_TRADE", "AR");
        await s.Rules.SetRoleMappingAsync(h.Admin, "AP_TRADE", "AP");
        await s.Rules.SetRoleMappingAsync(h.Admin, "CASH_DEFAULT", "CASH");
        var from = new DateOnly(2026, 1, 1);
        foreach (var (code, type, ev, def, tests) in new[]
                 {
                     ("CR-CUST", VoucherTypes.CashReceipt, "CUSTOMER_RECEIPT", CustomerReceiptDef, CustomerReceiptTests),
                     ("BR-CUST", VoucherTypes.BankReceipt, "CUSTOMER_RECEIPT", CustomerReceiptDef, BankReceiptTests),
                     ("BP-SUPP", VoucherTypes.BankPayment, "SUPPLIER_PAYMENT", SupplierPaymentDef, SupplierPaymentTests),
                     ("CP-EXP", VoucherTypes.CashPayment, "EXPENSE", ExpenseDef, ExpenseTests),
                     ("BP-XFER", VoucherTypes.BankPayment, "INTERNAL_TRANSFER", TransferDef, TransferTests),
                 })
        {
            var rule = await s.Rules.CreateDraftAsync(h.Admin, code, type, ev, 10, from, null, def, tests);
            var run = await s.Rules.ActivateAsync(h.Admin, rule.Id);
            if (!run.Passed) throw new InvalidOperationException($"{code}: {string.Join(" | ", run.Cases.Where(c => !c.Passed).Select(c => c.Detail))}");
        }
    }

    public static BusinessDocument Receipt(TestHost h, decimal amount, DateOnly date, Guid? settle = null, string partner = "KH01", string main = "CASH")
        => new(main == "CASH" ? VoucherTypes.CashReceipt : VoucherTypes.BankReceipt, "CUSTOMER_RECEIPT", date, date, "Thu tiền khách hàng",
            h.Partner[partner], h.Acc[main], [new BusinessDocumentLine(amount, SettleJournalLineId: settle)]);

    public static BusinessDocument Expense(TestHost h, decimal amount, DateOnly date, string counter = "EXP")
        => new(VoucherTypes.CashPayment, "EXPENSE", date, date, "Chi phí bằng tiền mặt", null, h.Acc["CASH"],
            [new BusinessDocumentLine(amount, h.Acc[counter])]);

    public static async Task<Voucher> PostDocAsync(TestHost h, Scope s, BusinessDocument doc)
    {
        var v = await s.Documents.CreateAsync(h.Preparer, doc);
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        await s.Engine.ApproveAsync(h.Approver, v.Id);
        await s.Engine.PostAsync(h.Approver, v.Id);
        return v;
    }

    /// <summary>Journal line id of the receivable/payable line created by a posted voucher.</summary>
    public static Guid LineOf(Scope s, Guid voucherId, Guid accountId)
        => s.Db.JournalLines.Single(l => l.AccountId == accountId && s.Db.JournalEntries.Any(e => e.Id == l.JournalEntryId && e.VoucherId == voucherId)).Id;
}
