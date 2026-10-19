using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Reports;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Reports;

public sealed record FinancialAnalysis(DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo, bool TemplatesVerified, IReadOnlyList<RatioRow> Ratios);

/// <summary>
/// Phân tích tài chính: chạy mẫu B01-DN và B02-DN đã nhập của công ty cho kỳ này và kỳ trước (cùng độ dài, liền trước) rồi tính các chỉ số theo mã số chỉ tiêu
/// (<see cref="FinancialRatios"/>). Chỉ đọc sổ đã ghi, không lưu. Không có mẫu thì báo rõ để nhập mẫu trước.
/// </summary>
public sealed class FinancialAnalysisService(HakDbContext db, ReportService reports)
{
    public async Task<FinancialAnalysis> AnalyseAsync(SessionContext ctx, DateOnly from, DateOnly to)
    {
        ctx.Require(Permissions.ReportView);
        if (to < from) throw new DomainException(ErrorCodes.ValidationFailed, "Đến ngày phải sau từ ngày.");
        var length = to.DayNumber - from.DayNumber + 1;
        var prevTo = from.AddDays(-1); var prevFrom = prevTo.AddDays(-(length - 1));

        async Task<(ReportRun Cur, ReportRun Prev, bool Verified)> RunAsync(string code)
        {
            var def = (await reports.ListAsync(ctx)).Where(d => d.Code == code).OrderByDescending(d => d.Version).FirstOrDefault()
                      ?? throw new DomainException(ErrorCodes.NotFound, $"Chưa nhập mẫu {code} (ở Tổng hợp → Báo cáo theo mẫu); cần mẫu để tính chỉ số.");
            var run = await reports.RunAsync(ctx, def.Id, from, to, prevFrom, prevTo);
            return (run, run, run.Verified);
        }
        var (b01, _, v1) = await RunAsync("B01-DN");
        var (b02, _, v2) = await RunAsync("B02-DN");

        static Func<string, decimal?> Pick(ReportRun r, bool compare)
        {
            var map = r.Lines.Where(l => l.Error is null).GroupBy(l => l.Code).ToDictionary(g => g.Key, g => compare ? g.First().CompareValue : g.First().Value);
            return code => map.GetValueOrDefault(code);
        }
        return new FinancialAnalysis(from, to, prevFrom, prevTo, v1 && v2,
            FinancialRatios.Compute(Pick(b01, true), Pick(b01, false), Pick(b02, true), Pick(b02, false), length, length));
    }
}
