using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Organization;
using Hak.Domain.Vouchers;

namespace Hak.Domain.Tests;

public class VoucherRulesTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid OtherCompany = Guid.NewGuid();
    private static readonly Account Cash = new() { Id = Guid.NewGuid(), CompanyId = Company, Code = "CASH" };
    private static readonly Account Ar = new() { Id = Guid.NewGuid(), CompanyId = Company, Code = "AR", RequiresPartner = true };
    private static readonly Account Header = new() { Id = Guid.NewGuid(), CompanyId = Company, Code = "HDR", IsPostable = false };
    private static readonly Account Closed = new() { Id = Guid.NewGuid(), CompanyId = Company, Code = "OLD", IsActive = false };
    private static readonly Account Foreign = new() { Id = Guid.NewGuid(), CompanyId = OtherCompany, Code = "X" };
    private static readonly BusinessPartner Customer = new() { Id = Guid.NewGuid(), CompanyId = Company, Code = "KH1" };
    private static readonly BusinessPartner ForeignCustomer = new() { Id = Guid.NewGuid(), CompanyId = OtherCompany, Code = "KH9" };

    private static readonly Dictionary<Guid, Account> Accounts = new[] { Cash, Ar, Header, Closed, Foreign }.ToDictionary(a => a.Id);
    private static readonly Dictionary<Guid, BusinessPartner> Partners = new[] { Customer, ForeignCustomer }.ToDictionary(p => p.Id);

    private static Voucher V(params VoucherLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++) lines[i].LineNo = i + 1;
        return new Voucher { CompanyId = Company, Number = "T1", Lines = [.. lines] };
    }

    private static VoucherLine L(Account a, Side s, decimal amt, BusinessPartner? p = null) => new() { AccountId = a.Id, Side = s, Amount = amt, PartnerId = p?.Id };

    private static List<string> Codes(Voucher v, int dp = 0) => VoucherValidator.Validate(v, Accounts, Partners, dp).Select(e => e.Code).ToList();

    [Fact]
    public void Balanced_IsValid()
        => Assert.Empty(Codes(V(L(Cash, Side.Debit, 1000), L(Ar, Side.Credit, 1000, Customer))));

    [Fact]
    public void MultiLine_DebitsAndCredits_Balance()
        => Assert.Empty(Codes(V(L(Cash, Side.Debit, 700), L(Cash, Side.Debit, 300), L(Ar, Side.Credit, 600, Customer), L(Ar, Side.Credit, 400, Customer))));

    [Fact] public void NoLines() => Assert.Contains(ErrorCodes.VoucherNoLines, Codes(V()));
    [Fact] public void Unbalanced() => Assert.Contains(ErrorCodes.VoucherUnbalanced, Codes(V(L(Cash, Side.Debit, 1000), L(Ar, Side.Credit, 999, Customer))));
    [Fact] public void OneSidedOnly() => Assert.Contains(ErrorCodes.VoucherUnbalanced, Codes(V(L(Cash, Side.Debit, 1000), L(Cash, Side.Debit, 1000))));
    [Fact] public void ZeroAmount() => Assert.Contains(ErrorCodes.AmountNotPositive, Codes(V(L(Cash, Side.Debit, 0), L(Cash, Side.Credit, 0))));
    [Fact] public void NegativeAmount() => Assert.Contains(ErrorCodes.AmountNotPositive, Codes(V(L(Cash, Side.Debit, -5), L(Cash, Side.Credit, -5))));
    [Fact] public void VndFraction() => Assert.Contains(ErrorCodes.AmountPrecision, Codes(V(L(Cash, Side.Debit, 10.5m), L(Cash, Side.Credit, 10.5m))));
    [Fact] public void UsdCents_AllowedWithTwoDecimals() => Assert.Empty(Codes(V(L(Cash, Side.Debit, 10.55m), L(Cash, Side.Credit, 10.55m)), dp: 2));
    [Fact] public void UnknownAccount() => Assert.Contains(ErrorCodes.AccountNotFound, Codes(V(new VoucherLine { AccountId = Guid.NewGuid(), Side = Side.Debit, Amount = 1 }, L(Cash, Side.Credit, 1))));
    [Fact] public void AccountOfOtherCompany() => Assert.Contains(ErrorCodes.AccountWrongCompany, Codes(V(L(Foreign, Side.Debit, 1), L(Cash, Side.Credit, 1))));
    [Fact] public void HeaderAccount() => Assert.Contains(ErrorCodes.AccountNotPostable, Codes(V(L(Header, Side.Debit, 1), L(Cash, Side.Credit, 1))));
    [Fact] public void InactiveAccount() => Assert.Contains(ErrorCodes.AccountInactive, Codes(V(L(Closed, Side.Debit, 1), L(Cash, Side.Credit, 1))));
    [Fact] public void PartnerRequired() => Assert.Contains(ErrorCodes.PartnerRequired, Codes(V(L(Cash, Side.Debit, 1), L(Ar, Side.Credit, 1))));
    [Fact] public void PartnerOfOtherCompany() => Assert.Contains(ErrorCodes.PartnerWrongCompany, Codes(V(L(Cash, Side.Debit, 1), L(Ar, Side.Credit, 1, ForeignCustomer))));

    [Fact]
    public void ForeignCurrency_ConvertedAmountMustMatchRoundedProduct()
    {
        var ok = L(Cash, Side.Debit, 31_290_540m);
        ok.ForeignCurrency = "USD"; ok.ForeignAmount = 1234.56m; ok.ExchangeRate = 25345.5m;
        Assert.Empty(Codes(V(ok, L(Ar, Side.Credit, 31_290_540m, Customer))));

        var bad = L(Cash, Side.Debit, 31_290_541m);
        bad.ForeignCurrency = "USD"; bad.ForeignAmount = 1234.56m; bad.ExchangeRate = 25345.5m;
        Assert.Contains(ErrorCodes.ValidationFailed, Codes(V(bad, L(Ar, Side.Credit, 31_290_541m, Customer))));
    }

    // ACC-08: số nguyên tệ theo đúng số chữ số lẻ của loại tiền; loại tiền phải có trong danh mục.
    private static readonly Dictionary<string, int> CurrencyDecimals = new() { ["VND"] = 0, ["USD"] = 2, ["JPY"] = 0 };

    private static List<string> ForeignCodes(string currency, decimal foreignAmount, decimal rate)
    {
        var amount = MoneyMath.Convert(foreignAmount, rate, 0);
        var l = L(Cash, Side.Debit, amount);
        l.ForeignCurrency = currency; l.ForeignAmount = foreignAmount; l.ExchangeRate = rate;
        return VoucherValidator.Validate(V(l, L(Ar, Side.Credit, amount, Customer)), Accounts, Partners, 0, CurrencyDecimals).Select(e => e.Code).ToList();
    }

    [Fact] public void ForeignAmount_WithinCurrencyDecimals_IsValid() => Assert.Empty(ForeignCodes("USD", 1234.56m, 25_000m));
    [Fact] public void ForeignAmount_TooManyDecimalsForUsd() => Assert.Contains(ErrorCodes.AmountPrecision, ForeignCodes("USD", 1234.567m, 25_000m));
    [Fact] public void ForeignAmount_DecimalsForZeroDecimalYen() => Assert.Contains(ErrorCodes.AmountPrecision, ForeignCodes("JPY", 1.5m, 170m));
    [Fact] public void ForeignCurrency_UnknownCode() => Assert.Contains(ErrorCodes.ValidationFailed, ForeignCodes("XYZ", 10m, 1_000m));

    [Fact]
    public void ForeignCurrency_IncompleteFieldsRejected()
    {
        var l = L(Cash, Side.Debit, 100);
        l.ForeignCurrency = "USD";
        Assert.Contains(ErrorCodes.ValidationFailed, Codes(V(l, L(Cash, Side.Credit, 100))));
    }

    [Fact]
    public void StateMachine_HappyPath()
    {
        var creator = Guid.NewGuid();
        var v = new Voucher { CreatedBy = creator };
        v.Submit(creator);
        v.Approve(Guid.NewGuid(), requireDistinctApprover: true);
        v.MarkPosted(Guid.NewGuid(), DateTime.UtcNow);
        v.MarkReversed();
        Assert.Equal(VoucherStatus.Reversed, v.Status);
    }

    [Fact]
    public void StateMachine_SegregationOfDuties()
    {
        var creator = Guid.NewGuid();
        var v = new Voucher { CreatedBy = creator };
        v.Submit(creator);
        Assert.Equal(ErrorCodes.SodViolation, Assert.Throws<DomainException>(() => v.Approve(creator, true)).Code);
        v.Approve(creator, requireDistinctApprover: false); // allowed only when the company disables SoD
    }

    [Fact]
    public void StateMachine_CannotPostDraft_OrEditPosted_OrPostTwice()
    {
        var v = new Voucher { CreatedBy = Guid.NewGuid() };
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<DomainException>(() => v.MarkPosted(Guid.NewGuid(), DateTime.UtcNow)).Code);
        v.Submit(v.CreatedBy); v.Approve(Guid.NewGuid(), true); v.MarkPosted(Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(ErrorCodes.LedgerImmutable, Assert.Throws<DomainException>(v.EnsureEditable).Code);
        Assert.Equal(ErrorCodes.AlreadyPosted, Assert.Throws<DomainException>(() => v.MarkPosted(Guid.NewGuid(), DateTime.UtcNow)).Code);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<DomainException>(v.Cancel).Code);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<DomainException>(v.Reject).Code);
    }

    [Fact]
    public void Periods_CalendarYear_Has12Months()
    {
        var fy = new FiscalYear { Id = Guid.NewGuid(), StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        var p = AccountingPeriod.MonthlyFor(fy);
        Assert.Equal(12, p.Count);
        Assert.Equal(new DateOnly(2026, 2, 28), p[1].EndDate);
        Assert.Equal(new DateOnly(2026, 12, 31), p[^1].EndDate);
    }

    [Fact]
    public void Periods_NonCalendarAndLongFirstYear_AreContiguous()
    {
        var fy = new FiscalYear { Id = Guid.NewGuid(), StartDate = new(2026, 4, 15), EndDate = new(2027, 6, 30) };
        var p = AccountingPeriod.MonthlyFor(fy);
        Assert.Equal(new DateOnly(2026, 4, 15), p[0].StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), p[0].EndDate);
        Assert.Equal(15, p.Count);
        for (var i = 1; i < p.Count; i++) Assert.Equal(p[i - 1].EndDate.AddDays(1), p[i].StartDate);
    }
}
