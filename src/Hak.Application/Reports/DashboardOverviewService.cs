using Hak.Application.Accounting;
using Hak.Application.Cycles;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Reports;

public sealed record MonthIncome(int Month, decimal Revenue, decimal NetSales, decimal Cost, decimal Profit);
public sealed record MonthCash(int Month, decimal Inflow, decimal Outflow, decimal Closing);
public sealed record CostPart(string Name, decimal Amount);
public sealed record ItemFigure(string Code, string Name, string Unit, decimal Quantity, decimal Value);

/// <summary>
/// Màn hình Tổng quan (kiểu thẻ tổng hợp): tình hình tài chính, doanh thu – chi phí – lợi nhuận theo tháng, dòng tiền theo tháng, hàng tồn kho lớn nhất,
/// mặt hàng bán chạy, cơ cấu chi phí. Doanh thu, chi phí, lợi nhuận lấy từ mẫu B02-DN đã nhập theo mã số chỉ tiêu (không tự phân loại tài khoản);
/// chưa có mẫu thì phần đó để trống (<see cref="HasIncomeTemplate"/> = false). Tiền, công nợ, tồn kho lấy thẳng từ sổ đã ghi.
/// </summary>
public sealed record DashboardOverview(DateOnly AsOf, int Year, decimal Cash, decimal CashOnHand, decimal Bank, decimal Receivable, decimal Payable, decimal Inventory,
    bool HasIncomeTemplate, bool IncomeTemplateVerified, IReadOnlyList<MonthIncome> Income, IReadOnlyList<MonthCash> CashFlow, IReadOnlyList<CostPart> CostBreakdown,
    IReadOnlyList<ItemFigure> InventoryTop, IReadOnlyList<ItemFigure> BestSellers);

public sealed class DashboardOverviewService(HakDbContext db, ReportService reports, LedgerQueryService ledger, StockService stock, SalesReportService sales)
{
    public async Task<DashboardOverview> OverviewAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var year = asOf.Year; var jan1 = new DateOnly(year, 1, 1);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.IsPostable).ToListAsync();

        // Tiền, công nợ
        decimal cashOnHand = 0, bank = 0, receivable = 0, payable = 0;
        foreach (var a in accounts)
        {
            if (a.CashKind == CashKind.None && a.OpenItemNature is not (OpenItemNature.Receivable or OpenItemNature.Payable)) continue;
            var bal = await ledger.AccountBalanceAsync(ctx, a.Id, asOf);
            if (a.CashKind == CashKind.Cash) cashOnHand += bal; else if (a.CashKind == CashKind.Bank) bank += bal;
            if (a.OpenItemNature == OpenItemNature.Receivable) receivable += bal;
            if (a.OpenItemNature == OpenItemNature.Payable) payable -= bal;
        }

        // Dòng tiền theo tháng của các tài khoản quỹ và ngân hàng
        var cashIds = accounts.Where(a => a.CashKind != CashKind.None).Select(a => a.Id).ToList();
        decimal running = 0;
        foreach (var id in cashIds) running += await ledger.AccountBalanceAsync(ctx, id, jan1.AddDays(-1));
        var lines = await db.JournalLines.AsNoTracking().Where(l => l.CompanyId == ctx.CompanyId && cashIds.Contains(l.AccountId) && l.PostingDate >= jan1 && l.PostingDate <= asOf)
            .Select(l => new { l.PostingDate.Month, l.Side, l.Amount }).ToListAsync();
        var cashFlow = new List<MonthCash>();
        for (var m = 1; m <= asOf.Month; m++)
        {
            var inflow = lines.Where(l => l.Month == m && l.Side == Side.Debit).Sum(l => l.Amount);
            var outflow = lines.Where(l => l.Month == m && l.Side == Side.Credit).Sum(l => l.Amount);
            running += inflow - outflow;
            cashFlow.Add(new MonthCash(m, inflow, outflow, running));
        }

        // Doanh thu, chi phí, lợi nhuận: chạy B02-DN từng tháng
        var income = new List<MonthIncome>(); var costParts = new Dictionary<string, decimal>(); var hasTemplate = false; var verified = false;
        var def = (await reports.ListAsync(ctx)).Where(d => d.Code == "B02-DN").OrderByDescending(d => d.Version).FirstOrDefault();
        if (def is not null)
        {
            hasTemplate = true; verified = def.Status == "Verified";
            for (var m = 1; m <= asOf.Month; m++)
            {
                var from = new DateOnly(year, m, 1); var to = m == asOf.Month ? asOf : from.AddMonths(1).AddDays(-1);
                var run = await reports.RunAsync(ctx, def.Id, from, to);
                decimal V(string code) => run.Lines.FirstOrDefault(l => l.Code == code && l.Error is null)?.Value ?? 0;
                var netSales = V("10");
                var revenue = netSales + V("21") + V("22") + V("31");
                var cost = V("11") + V("23") + V("25") + V("26") + V("32") + V("51") + V("52");
                income.Add(new MonthIncome(m, revenue, netSales, cost, revenue - cost));
                foreach (var (name, amount) in new[] { ("Giá vốn hàng bán", V("11")), ("Chi phí bán hàng", V("25")), ("Chi phí quản lý doanh nghiệp", V("26")), ("Chi phí tài chính", V("23")),
                             ("Chi phí khác", V("32")), ("Thuế thu nhập doanh nghiệp", V("51") + V("52")) })
                    costParts[name] = costParts.GetValueOrDefault(name) + amount;
            }
        }

        // Hàng tồn kho lớn nhất; mặt hàng bán chạy
        var summary = await stock.SummaryAsync(ctx, jan1, asOf);
        var byItem = summary.GroupBy(r => r.ItemId).Select(g => new ItemFigure(g.First().ItemCode, g.First().ItemName, g.First().Unit, g.Sum(r => r.ClosingQty), g.Sum(r => r.ClosingValue)))
            .Where(i => i.Quantity != 0 || i.Value != 0).OrderByDescending(i => i.Value).ToList();
        var analysis = await sales.AnalyseAsync(ctx, jan1, asOf);
        var best = analysis.ByItem.Where(r => r.Key != "").OrderByDescending(r => r.Revenue).Take(5).Select(r => new ItemFigure(r.Key, r.Name, "", r.Quantity, r.Revenue)).ToList();

        return new DashboardOverview(asOf, year, cashOnHand + bank, cashOnHand, bank, receivable, payable, byItem.Sum(i => i.Value), hasTemplate, verified, income, cashFlow,
            costParts.Where(p => p.Value != 0).Select(p => new CostPart(p.Key, p.Value)).OrderByDescending(p => p.Amount).ToList(), byItem.Take(5).ToList(), best);
    }
}
