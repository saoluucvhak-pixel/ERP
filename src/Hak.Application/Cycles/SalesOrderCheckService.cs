using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record SalesOrderStockLine(int LineNo, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal Ordered, decimal OnHand, decimal Shortfall);

/// <summary>
/// Kết quả "kiểm tra tín dụng và tồn kho" khi nhận đơn đặt hàng (tệp phân tích ERP, mục V.1.2.1): so hạn mức tín dụng của khách với
/// số dư công nợ hiện tại cộng giá trị ước tính của đơn; so số lượng đặt với tồn hiện tại. Thiếu hàng không chặn (đơn giao trễ).
/// </summary>
public sealed record SalesOrderCheck(Guid OrderId, string Number, Guid PartnerId, string PartnerName, decimal? CreditLimit, bool CreditCheckApplied, string? CreditCheckNote,
    decimal CurrentBalance, decimal OverdueAmount, decimal OrderValue, decimal BalanceAfter, bool ExceedsCredit, IReadOnlyList<SalesOrderStockLine> Lines, bool AnyShortage);

public sealed class SalesOrderCheckService(HakDbContext db, SettlementService settlement, IClock clock)
{
    public async Task<SalesOrderCheck> CheckAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.VoucherView);
        var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Đơn hàng không tồn tại.");
        if (order.Kind != InvoiceKind.Sales) throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ kiểm tra tín dụng và tồn kho cho đơn bán.");
        return await ComputeAsync(ctx, order);
    }

    /// <summary>Used by order confirmation inside its transaction; <paramref name="order"/> must have its lines loaded.</summary>
    public async Task<SalesOrderCheck> ComputeAsync(SessionContext ctx, TradeOrder order)
    {
        var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == order.PartnerId);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var terms = await Hak.Application.Setup.GroupDefaults.PartnerTermsAsync(db, ctx.CompanyId, partner);
        var creditLimit = terms.CreditLimit;

        // Estimated order value incl. VAT, in functional currency (VND, rounded per line). Foreign-currency orders are not compared with the VND limit.
        var value = order.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice * (1 + (l.VatRate ?? 0) / 100m), 0, MidpointRounding.AwayFromZero));
        decimal balance = 0, overdue = 0;
        string? note = null;
        var applied = creditLimit is not null && order.Currency is null;
        if (creditLimit is null) note = "Khách hàng chưa khai báo hạn mức tín dụng: không kiểm tra.";
        else if (order.Currency is not null) note = "Đơn ngoại tệ: không so với hạn mức VND.";
        else
        {
            var ar = (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "AR_TRADE"))?.AccountId;
            if (ar is null) { applied = false; note = "Chưa ánh xạ vai trò AR_TRADE: không tính được công nợ."; }
            else
            {
                var open = await settlement.OpenItemsAsync(ctx, ar.Value, partner.Id, today);
                balance = open.Where(o => o.Side == Hak.Domain.Vouchers.Side.Debit).Sum(o => o.Open) - open.Where(o => o.Side == Hak.Domain.Vouchers.Side.Credit).Sum(o => o.Open);
                overdue = open.Where(o => o.Side == Hak.Domain.Vouchers.Side.Debit && o.DueDate < today).Sum(o => o.Open);
            }
        }

        var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && itemIds.Contains(m.ItemId)).Select(m => new { m.ItemId, m.Quantity }).ToListAsync();
        var onHand = moves.GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var lines = order.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var have = onHand.GetValueOrDefault(l.ItemId);
            return new SalesOrderStockLine(l.LineNo, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, items[l.ItemId].Unit, l.Quantity, have, items[l.ItemId].IsService ? 0 : Math.Max(0, l.Quantity - have));
        }).ToList();

        var after = balance + value;
        return new SalesOrderCheck(order.Id, order.Number, partner.Id, partner.Name, creditLimit, applied, note, balance, overdue, value, after,
            applied && after > creditLimit!.Value, lines, lines.Any(l => l.Shortfall > 0));
    }
}
