using Hak.Domain.Common;

namespace Hak.Domain.Assets;

/// <summary>TSCĐ (depreciated, with an accumulated-depreciation account) or CCDC / prepaid cost (allocated straight off its own account).</summary>
public enum AssetClass { FixedAsset = 1, Tool = 2 }

/// <summary>
/// Phương pháp khấu hao / phân bổ chọn theo từng thẻ (căn cứ pháp lý từng phương pháp: [CẦN XÁC MINH]).
/// StraightLine: đường thẳng theo tháng, tháng đầu / cuối theo <see cref="ProrationPolicy"/>.
/// TimeBased: theo số ngày thực tế từ ngày bắt đầu đến ngày kết thúc phân bổ (ví dụ bảo hiểm 15/03 đến 14/03 năm sau).
/// UnitsOfProduction: theo sản lượng — số phân bổ lũy kế = nguyên giá × sản lượng thực tế lũy kế ÷ tổng sản lượng dự kiến; sản lượng nhập theo từng kỳ.
/// DecliningBalance: số dư giảm dần có điều chỉnh — mỗi năm = giá trị còn lại đầu năm × tỷ lệ đường thẳng × hệ số; khi mức này nhỏ hơn mức đường thẳng của phần còn lại thì chuyển sang đường thẳng.
/// </summary>
public enum DepreciationMethod { StraightLine = 1, TimeBased = 2, UnitsOfProduction = 3, DecliningBalance = 4 }

/// <summary>
/// How the first and last month are counted. [CẦN XÁC MINH] which rule the company applies under the current regulation.
/// FullMonth: the in-service month counts fully, the disposal month does not count.
/// Daily: by days used (start day and the day before disposal inclusive) over the days in the month.
/// </summary>
public enum ProrationPolicy { FullMonth = 1, Daily = 2 }

/// <summary>
/// Kỳ ghi sổ khấu hao / phân bổ của một thẻ: hàng tháng (mặc định), hàng quý hoặc hàng năm (theo năm tài chính của công ty).
/// Số phải ghi mỗi kỳ vẫn là lũy kế theo kế hoạch trừ số đã ghi, nên ghi theo quý hoặc năm thì dồn đủ số các tháng trước đó; thẻ kết thúc giữa quý / năm thì ghi nốt phần còn lại ở tháng kết thúc.
/// </summary>
public enum AllocationFrequency { Monthly = 1, Quarterly = 2, Yearly = 3 }

public enum AssetStatus { Draft = 1, Active = 2, Disposed = 3, Cancelled = 9 }

/// <summary>
/// Asset card. Its cost is never booked by this module: it is the sum of already-posted journal lines on the asset account
/// (<see cref="AssetCostSource"/>), so the register and the ledger cannot count the same cost twice.
/// Parameters are frozen once Active; corrections go through disposal reversal or (later) a change document.
/// </summary>
public sealed class LongTermAsset
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AssetClass Class { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Draft;
    /// <summary>Cost account (TSCĐ) or prepaid/tool account (CCDC). Taken from the catalog, never hard-coded.</summary>
    public Guid AssetAccountId { get; set; }
    /// <summary>Accumulated depreciation account; required for fixed assets, null for tools (credited straight to the asset account).</summary>
    public Guid? AccumulatedAccountId { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    public ProrationPolicy Proration { get; set; } = ProrationPolicy.FullMonth;
    /// <summary>Ghi sổ hàng tháng, hàng quý hoặc hàng năm. Đóng băng khi thẻ đã ghi tăng.</summary>
    public AllocationFrequency AllocationFrequency { get; set; } = AllocationFrequency.Monthly;
    /// <summary>Frozen at activation: Σ cost sources.</summary>
    public decimal OriginalCost { get; set; }
    public decimal ResidualValue { get; set; }
    public int UsefulLifeMonths { get; set; }
    /// <summary>Ngày đưa vào sử dụng / bắt đầu phân bổ.</summary>
    public DateOnly InServiceDate { get; set; }
    /// <summary>
    /// Assets taken over from another system: depreciation already accumulated before <see cref="ScheduleStartDate"/>
    /// (it sits in the opening-balance voucher) and the life still remaining from that date.
    /// </summary>
    public decimal OpeningAccumulated { get; set; }
    public int? RemainingLifeMonths { get; set; }
    public DateOnly ScheduleStartDate { get; set; }
    /// <summary>Ngày cuối cùng của thời gian phân bổ — phương pháp theo thời gian.</summary>
    public DateOnly? ScheduleEndDate { get; set; }
    /// <summary>Hệ số điều chỉnh của phương pháp số dư giảm dần (để trống = tự chọn theo thời gian sử dụng: ≤ 4 năm 1,5; trên 4 đến 6 năm 2,0; trên 6 năm 2,5).</summary>
    public decimal? DecliningFactor { get; set; }
    /// <summary>Tổng sản lượng dự kiến (phương pháp theo sản lượng) và đơn vị sản lượng.</summary>
    public decimal? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public DateOnly? DisposalDate { get; set; }
    public Guid? DisposalVoucherId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public Guid? ActivatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<AssetCostSource> CostSources { get; set; } = [];

    /// <summary>Amount the schedule still has to depreciate from <see cref="ScheduleStartDate"/>.</summary>
    public decimal ScheduleBase => OriginalCost - ResidualValue - OpeningAccumulated;

    /// <summary>Số kỳ tháng của lịch để hiển thị: số tháng sử dụng, hoặc số tháng dương lịch phủ khoảng ngày của phương pháp theo thời gian.</summary>
    public int ScheduleSpanMonths => Method == DepreciationMethod.TimeBased && ScheduleEndDate is { } e
        ? (e.Year * 12 + e.Month) - (ScheduleStartDate.Year * 12 + ScheduleStartDate.Month) + 1
        : ScheduleMonths;
    public int ScheduleMonths => RemainingLifeMonths ?? UsefulLifeMonths;
}

/// <summary>Part of a posted journal line (debit on the asset account) that makes up an asset's cost.</summary>
public sealed class AssetCostSource
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public Guid JournalLineId { get; set; }
    public decimal Amount { get; set; }
}

public enum DepreciationRunStatus { Active = 1, Reversed = 2 }

/// <summary>Depreciation / allocation of all active assets for one accounting period, with the GL voucher it produced.</summary>
public sealed class DepreciationRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PeriodId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DepreciationRunStatus Status { get; set; } = DepreciationRunStatus.Active;
    public Guid? VoucherId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
}

/// <summary>
/// One asset's depreciation for a run, or its final part booked by a disposal (RunId null, IsDisposal).
/// Append-only; voided once when the run or disposal is reversed.
/// </summary>
public sealed class DepreciationEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public Guid? RunId { get; set; }
    public bool IsDisposal { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
}

/// <summary>
/// Pure straight-line schedule. Each period books (cumulative target at period end) − (already booked), so rounding never
/// accumulates and the last month always closes exactly at the schedule base. Rounding: MidpointRounding.AwayFromZero.
/// </summary>
public static class DepreciationCalculator
{
    /// <summary>
    /// Kỳ này có phải ghi sổ không: luôn ghi nếu thẻ ghi hàng tháng; hàng quý thì ở tháng cuối quý, hàng năm thì ở tháng cuối năm (tính từ ngày đầu năm tài chính);
    /// và luôn ghi nốt khi kế hoạch hoàn tất trong kỳ này (<paramref name="planComplete"/>), để thẻ kết thúc giữa quý / năm không bị bỏ sót.
    /// </summary>
    public static bool IsBookingPeriod(AllocationFrequency frequency, DateOnly fiscalYearStart, DateOnly fiscalYearEnd, DateOnly periodEnd, bool planComplete)
    {
        if (frequency == AllocationFrequency.Monthly || planComplete) return true;
        if (frequency == AllocationFrequency.Yearly) return periodEnd >= fiscalYearEnd;
        var monthsIn = (periodEnd.Year * 12 + periodEnd.Month) - (fiscalYearStart.Year * 12 + fiscalYearStart.Month) + 1;
        return monthsIn % 3 == 0 || periodEnd >= fiscalYearEnd;
    }

    /// <summary>
    /// Depreciation that should have been booked from <paramref name="start"/> up to and including <paramref name="through"/>.
    /// </summary>
    public static decimal CumulativeTarget(decimal scheduleBase, int months, DateOnly start, DateOnly through, ProrationPolicy proration, int decimals)
    {
        if (months <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Thời gian khấu hao phải lớn hơn 0 tháng.");
        if (scheduleBase <= 0 || through < start) return 0;
        var (num, den) = Elapsed(start, through, proration);
        // elapsed = num / den months; target = base × elapsed / months, computed as one exact division before rounding.
        if (num >= (long)months * den) return scheduleBase;
        return MoneyMath.Round(scheduleBase * num / (den * (decimal)months), decimals);
    }

    /// <summary>Theo thời gian: tỷ lệ số ngày đã qua (từ <paramref name="start"/> đến <paramref name="through"/>) trên tổng số ngày đến <paramref name="end"/>; một phép chia chính xác rồi mới làm tròn.</summary>
    public static decimal CumulativeTargetByTime(decimal scheduleBase, DateOnly start, DateOnly end, DateOnly through, int decimals)
    {
        if (end < start) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày kết thúc phân bổ không được trước ngày bắt đầu.");
        if (scheduleBase <= 0 || through < start) return 0;
        if (through >= end) return scheduleBase;
        long total = end.DayNumber - start.DayNumber + 1, elapsed = through.DayNumber - start.DayNumber + 1;
        return MoneyMath.Round(scheduleBase * elapsed / total, decimals);
    }

    /// <summary>Theo sản lượng: nguyên giá × sản lượng lũy kế ÷ tổng sản lượng dự kiến (đủ hoặc vượt tổng thì phân bổ hết).</summary>
    public static decimal CumulativeTargetByUnits(decimal scheduleBase, decimal totalUnits, decimal cumulativeUnits, int decimals)
    {
        if (totalUnits <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Tổng sản lượng dự kiến phải lớn hơn 0.");
        if (scheduleBase <= 0 || cumulativeUnits <= 0) return 0;
        if (cumulativeUnits >= totalUnits) return scheduleBase;
        return MoneyMath.Round(scheduleBase * cumulativeUnits / totalUnits, decimals);
    }

    /// <summary>Hệ số mặc định của số dư giảm dần theo thời gian sử dụng (tháng): ≤ 48 → 1,5; ≤ 72 → 2,0; còn lại 2,5. [CẦN XÁC MINH]</summary>
    public static decimal DefaultDecliningFactor(int months) => months <= 48 ? 1.5m : months <= 72 ? 2.0m : 2.5m;

    /// <summary>
    /// Số phải khấu hao theo từng năm (12 tháng; năm cuối có thể ngắn hơn) của phương pháp số dư giảm dần có điều chỉnh: mỗi năm = giá trị còn lại đầu năm × (hệ số ÷ số năm sử dụng);
    /// khi mức đó nhỏ hơn mức đường thẳng của phần còn lại (còn lại ÷ số năm còn lại) thì các năm còn lại chia đều theo đường thẳng. Tổng các năm đúng bằng <paramref name="scheduleBase"/>.
    /// </summary>
    public static List<(int Months, decimal Amount)> DecliningYears(decimal scheduleBase, int months, decimal factor)
    {
        if (months <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Thời gian khấu hao phải lớn hơn 0 tháng.");
        if (factor <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Hệ số số dư giảm dần phải lớn hơn 0.");
        var years = new List<(int, decimal)>();
        var rate = factor * 12m / months;
        var rem = scheduleBase;
        for (var done = 0; done < months;)
        {
            var left = months - done;
            if (left <= 12) { years.Add((left, rem)); break; }
            var remYears = left / 12m;
            var straight = rem / remYears;
            var declining = rem * rate;
            if (declining >= straight) { years.Add((12, declining)); rem -= declining; done += 12; continue; }
            for (var m = left; m > 0; m -= 12) years.Add((Math.Min(12, m), straight * Math.Min(12, m) / 12m));   // chuyển sang đường thẳng cho phần còn lại
            break;
        }
        return years;
    }

    /// <summary>Số dư giảm dần: lũy kế đến <paramref name="through"/>; trong năm chia đều theo tháng (tháng đầu / cuối theo <paramref name="proration"/>).</summary>
    public static decimal CumulativeTargetDeclining(decimal scheduleBase, int months, DateOnly start, DateOnly through, ProrationPolicy proration, decimal factor, int decimals)
    {
        if (scheduleBase <= 0 || through < start) return 0;
        var (num, den) = Elapsed(start, through, proration);
        if (num >= (long)months * den) return scheduleBase;
        var elapsed = (decimal)num / den;
        decimal cum = 0, covered = 0;
        foreach (var (m, amount) in DecliningYears(scheduleBase, months, factor))
        {
            if (elapsed >= covered + m) { cum += amount; covered += m; continue; }
            cum += amount * (elapsed - covered) / m;
            break;
        }
        return MoneyMath.Round(cum, decimals);
    }

    /// <summary>Số phải khấu hao / phân bổ lũy kế đến hết <paramref name="through"/> theo phương pháp của thẻ. <paramref name="cumulativeUnits"/>: sản lượng thực tế lũy kế (chỉ phương pháp theo sản lượng).</summary>
    public static decimal Target(LongTermAsset a, DateOnly through, int decimals, decimal cumulativeUnits = 0) => a.Method switch
    {
        DepreciationMethod.TimeBased => CumulativeTargetByTime(a.ScheduleBase, a.ScheduleStartDate,
            a.ScheduleEndDate ?? throw new DomainException(ErrorCodes.ValidationFailed, $"Thẻ {a.Code} chưa có ngày kết thúc phân bổ."), through, decimals),
        DepreciationMethod.UnitsOfProduction => CumulativeTargetByUnits(a.ScheduleBase,
            a.TotalUnits ?? throw new DomainException(ErrorCodes.ValidationFailed, $"Thẻ {a.Code} chưa có tổng sản lượng dự kiến."), cumulativeUnits, decimals),
        DepreciationMethod.DecliningBalance => CumulativeTargetDeclining(a.ScheduleBase, a.ScheduleMonths, a.ScheduleStartDate, through, a.Proration,
            a.DecliningFactor ?? DefaultDecliningFactor(a.ScheduleMonths), decimals),
        _ => CumulativeTarget(a.ScheduleBase, a.ScheduleMonths, a.ScheduleStartDate, through, a.Proration, decimals),
    };

    /// <summary>Ngày cuối cùng còn khấu hao khi ghi giảm: theo thời gian đến hết ngày trước ngày ghi giảm; theo sản lượng đến hết tháng trước (sản lượng tháng ghi giảm chưa có); còn lại theo chính sách tháng đầu / cuối.</summary>
    public static DateOnly LastDay(LongTermAsset a, DateOnly disposalDate) => a.Method switch
    {
        DepreciationMethod.TimeBased => disposalDate.AddDays(-1),
        DepreciationMethod.UnitsOfProduction => new DateOnly(disposalDate.Year, disposalDate.Month, 1).AddDays(-1),
        _ => LastDepreciationDay(disposalDate, a.Proration),
    };

    /// <summary>Elapsed months as an exact fraction num/den.</summary>
    internal static (long Num, long Den) Elapsed(DateOnly start, DateOnly through, ProrationPolicy proration)
    {
        var monthIndex = (through.Year * 12 + through.Month) - (start.Year * 12 + start.Month);
        if (proration == ProrationPolicy.FullMonth) return (monthIndex + 1, 1);

        long d1 = DateTime.DaysInMonth(start.Year, start.Month);
        if (monthIndex == 0) return (through.Day - start.Day + 1, d1);
        long d2 = DateTime.DaysInMonth(through.Year, through.Month);
        long first = d1 - start.Day + 1;       // days used in the first month, over d1
        long last = through.Day;               // days used in the last month, over d2
        long full = monthIndex - 1;            // whole months in between
        return (first * d2 + full * d1 * d2 + last * d1, d1 * d2);
    }

    /// <summary>Last day the asset depreciates when disposed on <paramref name="disposalDate"/>.</summary>
    public static DateOnly LastDepreciationDay(DateOnly disposalDate, ProrationPolicy proration)
        => proration == ProrationPolicy.FullMonth ? new DateOnly(disposalDate.Year, disposalDate.Month, 1).AddDays(-1) : disposalDate.AddDays(-1);
}

/// <summary>
/// Chia chi phí khấu hao / phân bổ của một thẻ cho nhiều bộ phận hưởng lợi (ví dụ bảo hiểm, tiền thuê nhà xưởng trả trước). Mỗi dòng là một tài khoản chi phí (và nhãn bộ phận) kèm tỷ lệ %;
/// tổng các dòng đúng 100. Không có dòng nào thì dùng tài khoản chi phí của thẻ. Đổi cách chia chỉ áp dụng cho các kỳ chạy sau; các kỳ đã chạy giữ nguyên (xem <see cref="DepreciationEntrySplit"/>).
/// </summary>
public sealed class AssetExpenseSplit
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public Guid ExpenseAccountId { get; set; }
    /// <summary>Tên bộ phận / đối tượng hưởng lợi (hiển thị trên diễn giải bút toán và báo cáo).</summary>
    public string? Label { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>Phần chi phí của một dòng khấu hao / phân bổ chia cho từng tài khoản và bộ phận, ghi lại lúc chạy kỳ. Chỉ thêm, không sửa, không xóa; bị hủy cùng dòng khấu hao cha.</summary>
public sealed class DepreciationEntrySplit
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public string? Label { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Chia một số tiền theo tỷ lệ phần trăm: mỗi phần làm tròn tới <c>decimals</c> chữ số, phần chênh do làm tròn dồn vào dòng có tỷ lệ lớn nhất (dòng đầu khi bằng nhau) nên tổng luôn đúng số gốc.</summary>
public static class ExpenseSplitter
{
    public static List<(int Index, decimal Amount)> Split(decimal amount, IReadOnlyList<decimal> percents, int decimals)
    {
        if (percents.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Chưa có tỷ lệ chia.");
        if (percents.Sum() != 100m) throw new DomainException(ErrorCodes.ValidationFailed, "Tổng tỷ lệ chia phải đúng 100%.");
        var parts = percents.Select((p, i) => (Index: i, Amount: MoneyMath.Round(amount * p / 100m, decimals))).ToList();
        var diff = amount - parts.Sum(x => x.Amount);
        if (diff != 0)
        {
            var big = percents.Select((p, i) => (p, i)).OrderByDescending(x => x.p).ThenBy(x => x.i).First().i;
            parts[big] = (big, parts[big].Amount + diff);
        }
        return parts;
    }
}

/// <summary>Sản lượng thực tế của một thẻ trong một kỳ kế toán (phương pháp theo sản lượng). Sửa được cho đến khi kỳ đó được chạy khấu hao; hủy kỳ thì sửa lại được.</summary>
public sealed class AssetUsage
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public Guid PeriodId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime RecordedAt { get; set; }
    public Guid RecordedBy { get; set; }
}
