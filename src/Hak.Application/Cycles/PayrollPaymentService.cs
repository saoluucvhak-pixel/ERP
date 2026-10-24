using Hak.Application.Audit;
using Hak.Application.Banking;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record PayrollPayableRow(Guid JournalLineId, string VoucherNumber, DateOnly PostingDate, Guid EmployeeId, string EmployeeCode, string EmployeeName,
    decimal Net, decimal Open, string? Description, string? PaymentOrderNumber, string? PaymentOrderStatus);

/// <summary>
/// Chu trình nhân sự – lương bàn giao sang thanh toán (tệp phân tích ERP, mục V.3): phiếu lương đã ghi sổ để lại phải trả người lao động theo từng người
/// (vai trò TK PAYROLL_PAYABLE); hàng chờ trả lương liệt kê các khoản còn nợ chưa có lệnh chi; lập lệnh thanh toán lương (sự kiện PAYROLL_PAYMENT) ở
/// trạng thái Nháp, người khác duyệt như mọi lệnh chi. Khoản đã có lệnh còn hiệu lực không lập lại. Quy tắc hạch toán của sự kiện PAYROLL_PAYMENT do kế toán khai báo [CẦN XÁC MINH].
/// </summary>
public sealed class PayrollPaymentService(HakDbContext db, SettlementService settlement, PaymentOrderService orders, AuditService audit)
{
    public const string BusinessEvent = "PAYROLL_PAYMENT";

    public async Task<IReadOnlyList<PayrollPayableRow>> QueueAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        // Hàng chờ trả lương lộ số thực lĩnh TỪNG NGƯỜI: chỉ người xem được dữ liệu nhạy cảm hoặc người lập lệnh chi (nghiệp vụ ngân hàng) mới thấy.
        if (!ctx.Permissions.Contains(Permissions.SensitiveView) && !ctx.Permissions.Contains(Permissions.BankDocument))
            throw new DomainException(ErrorCodes.Forbidden, "Hàng chờ trả lương cần quyền xem thông tin nhạy cảm hoặc quyền nghiệp vụ ngân hàng.");
        var role = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "PAYROLL_PAYABLE");
        if (role is null) return [];
        var open = await settlement.OpenItemsAsync(ctx, role.AccountId, null, asOf);
        var lineIds = open.Select(o => o.JournalLineId).ToList();
        var active = (await db.PaymentOrders.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId && p.SettleJournalLineId != null && lineIds.Contains(p.SettleJournalLineId!.Value)
                                                                      && p.Status != PaymentOrderStatus.Cancelled).ToListAsync())
            .GroupBy(p => p.SettleJournalLineId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        return open.Where(o => o.Side == Side.Credit)
            .Select(o => new PayrollPayableRow(o.JournalLineId, o.VoucherNumber, o.PostingDate, o.PartnerId, partners[o.PartnerId].Code, partners[o.PartnerId].Name, o.Amount, o.Open,
                o.Description, active.GetValueOrDefault(o.JournalLineId)?.Number, active.GetValueOrDefault(o.JournalLineId)?.Status.ToString()))
            .OrderBy(r => r.PostingDate).ThenBy(r => r.EmployeeCode, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lập lệnh thanh toán lương Nháp cho các khoản chọn (mỗi khoản một lệnh, đúng số còn nợ). Trả về các lệnh đã lập.</summary>
    public async Task<IReadOnlyList<PaymentOrder>> CreateOrdersAsync(SessionContext ctx, Guid bankAccountId, IReadOnlyList<Guid> journalLineIds, DateOnly asOf)
    {
        ctx.Require(Permissions.BankDocument);
        if (journalLineIds.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Chưa chọn khoản lương nào.");
        return await db.InTransactionAsync(async () =>
        {
            var queue = (await QueueAsync(ctx, asOf)).ToDictionary(r => r.JournalLineId);
            var created = new List<PaymentOrder>();
            foreach (var id in journalLineIds.Distinct())
            {
                if (!queue.TryGetValue(id, out var row)) throw new DomainException(ErrorCodes.NotFound, "Khoản phải trả lương không còn mở hoặc không tồn tại.");
                if (row.PaymentOrderNumber != null) throw new DomainException(ErrorCodes.DuplicateSource, $"{row.EmployeeCode} đã có lệnh {row.PaymentOrderNumber} còn hiệu lực.");
                created.Add(await orders.CreateAsync(ctx, new PaymentOrderInput(bankAccountId, row.EmployeeId, row.Open, asOf,
                    $"Trả lương {row.EmployeeCode} – {row.VoucherNumber}", SettleJournalLineId: row.JournalLineId, BusinessEvent: BusinessEvent)));
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "payroll_payment.create_orders", "payment_order", created[0].Id.ToString(), new { count = created.Count, total = created.Sum(o => o.Amount) });
            await db.SaveChangesAsync();
            return (IReadOnlyList<PaymentOrder>)created;
        });
    }
}
