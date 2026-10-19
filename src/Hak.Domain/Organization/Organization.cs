namespace Hak.Domain.Organization;

public sealed class Company
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TaxCode { get; set; }
    /// <summary>ISO 4217 functional (accounting) currency.</summary>
    public string FunctionalCurrency { get; set; } = "VND";
    public Guid RegimeVersionId { get; set; }
    /// <summary>Segregation of duties: approver must differ from creator.</summary>
    public bool RequireDistinctApprover { get; set; } = true;
    /// <summary>Nhà cung cấp mới tạo ở trạng thái chờ duyệt, chưa được đặt hàng cho tới khi người có thẩm quyền duyệt.</summary>
    public bool RequireSupplierApproval { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}

public sealed class Branch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}

public sealed class Currency
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DecimalPlaces { get; set; }
}

public sealed class FiscalYear
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public List<AccountingPeriod> Periods { get; set; } = [];

    public bool Contains(DateOnly d) => d >= StartDate && d <= EndDate;
    public bool Overlaps(DateOnly start, DateOnly end) => start <= EndDate && end >= StartDate;
}

public enum PeriodStatus { Open = 1, Locked = 2 }

public sealed class AccountingPeriod
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public int PeriodNo { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public DateTime? LockedAt { get; set; }
    public Guid? LockedBy { get; set; }
    public long RowVersion { get; set; }

    public bool Contains(DateOnly d) => d >= StartDate && d <= EndDate;

    /// <summary>Calendar-month periods covering [start, end]; first/last may be partial.</summary>
    public static List<AccountingPeriod> MonthlyFor(FiscalYear fy)
    {
        var list = new List<AccountingPeriod>();
        var cursor = fy.StartDate;
        var no = 1;
        while (cursor <= fy.EndDate)
        {
            var monthEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var end = monthEnd < fy.EndDate ? monthEnd : fy.EndDate;
            list.Add(new AccountingPeriod
            {
                Id = Common.Ids.New(), CompanyId = fy.CompanyId, FiscalYearId = fy.Id,
                PeriodNo = no++, StartDate = cursor, EndDate = end,
            });
            cursor = end.AddDays(1);
        }
        return list;
    }
}
