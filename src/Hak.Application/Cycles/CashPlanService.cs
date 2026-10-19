using Hak.Application.Loans;
using Hak.Application.Accounting;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <summary>Một khoảng thời gian của kế hoạch tiền: thu dự kiến (hóa đơn bán đến hạn), chi dự kiến (hóa đơn mua đến hạn) và số dư tiền lũy kế.</summary>
public sealed record CashPlanBucket(string Label, DateOnly? From, DateOnly? To, decimal Inflow, int InflowDocs, decimal Outflow, decimal OutflowAccepted, int OutflowDocs, decimal Net, decimal ClosingCash);
public sealed record CashPlan(DateOnly AsOf, decimal OpeningCash, IReadOnlyList<CashPlanBucket> Buckets, decimal TotalInflow, decimal TotalOutflow, decimal EndingCash);

/// <summary>
/// Kế hoạch tiền theo ngày đáo hạn (tệp phân tích ERP, mục V.2.2.3: "cung cấp thông tin để lập kế hoạch ngân sách phục vụ chuẩn bị tiền thanh toán
/// tại từng thời điểm"). Số dư tiền đầu kỳ là số dư đã ghi sổ của các tài khoản quỹ và ngân hàng; thu và chi dự kiến lấy từ hóa đơn bán và mua VND
/// còn nợ và lịch trả nợ vay VND, xếp theo ngày đáo hạn (quá hạn và đến hạn trong ngày vào cột "Quá hạn / đến hạn", không có hạn vào cột "Chưa có hạn").
/// Đây là dự báo từ hạn thanh toán, không phải cam kết; chi đã chấp nhận thanh toán được tách riêng.
/// </summary>
public sealed class CashPlanService(HakDbContext db, LedgerQueryService ledger, SalesReportService sales, PaymentAcceptanceService payables, LoanScheduleService loans)
{
    public async Task<CashPlan> PlanAsync(SessionContext ctx, DateOnly asOf, int weeks = 8)
    {
        ctx.Require(Permissions.ReportView);
        if (weeks is < 1 or > 52) throw new DomainException(ErrorCodes.ValidationFailed, "Số tuần từ 1 đến 52.");
        var cashAccounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.CashKind != CashKind.None && a.IsPostable).Select(a => a.Id).ToListAsync();
        decimal opening = 0;
        foreach (var id in cashAccounts) opening += await ledger.AccountBalanceAsync(ctx, id, asOf);

        var inflow = (await sales.ReceivableQueueAsync(ctx, asOf)).Select(r => (Due: r.DueDate, r.Open, Accepted: false)).ToList();
        var outflow = (await payables.PayableQueueAsync(ctx, asOf)).Select(r => (Due: r.DueDate, r.Open, Accepted: r.Stage != PaymentAcceptanceService.Stages.NotAccepted)).ToList();
        // Lịch trả nợ vay (gốc còn phải trả + lãi kỳ) là chi dự kiến đã có hạn cố định, coi như đã cam kết.
        outflow.AddRange((await loans.UpcomingAsync(ctx, asOf)).Select(r => (Due: (DateOnly?)r.DueDate, Open: r.Total, Accepted: true)));

        // Buckets: [<= asOf] then consecutive 7-day weeks, then later, then no due date.
        var defs = new List<(string Label, DateOnly? From, DateOnly? To)> { ("Quá hạn / đến hạn hôm nay", null, asOf) };
        for (var w = 0; w < weeks; w++)
        {
            var from = asOf.AddDays(1 + 7 * w); var to = from.AddDays(6);
            defs.Add(($"Tuần {w + 1} ({from:dd/MM}–{to:dd/MM})", from, to));
        }
        var last = asOf.AddDays(7 * weeks);
        defs.Add(("Sau đó", last.AddDays(1), null));
        defs.Add(("Chưa có hạn thanh toán", null, null));

        static bool In((DateOnly? From, DateOnly? To) d, DateOnly? due, int index, int count)
            => index == count - 1 ? due is null                    // last bucket: no due date
             : due is null ? false
             : (d.From is null || due >= d.From) && (d.To is null || due <= d.To);

        var buckets = new List<CashPlanBucket>();
        var cash = opening;
        for (var i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            var inn = inflow.Where(x => In((d.From, d.To), x.Due, i, defs.Count)).ToList();
            var outt = outflow.Where(x => In((d.From, d.To), x.Due, i, defs.Count)).ToList();
            var inSum = inn.Sum(x => x.Open); var outSum = outt.Sum(x => x.Open);
            cash += inSum - outSum;
            buckets.Add(new CashPlanBucket(d.Label, d.From, d.To, inSum, inn.Count, outSum, outt.Where(x => x.Accepted).Sum(x => x.Open), outt.Count, inSum - outSum, cash));
        }
        return new CashPlan(asOf, opening, buckets, inflow.Sum(x => x.Open), outflow.Sum(x => x.Open), cash);
    }
}
