using Hak.Application.Security;
using Hak.Domain.Common;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed partial class InvoiceService
{
    /// <summary>
    /// Chính sách duyệt trả hàng / giảm giá bán: khi công ty bật, tổng đã trả (hoặc đã giảm) trên dòng hóa đơn gốc sau thao tác này không được vượt tổng số lượng (số tiền)
    /// đã được người có thẩm quyền duyệt cho dòng đó. Phần đã dùng lấy từ chính các chứng từ trả hàng / hóa đơn điều chỉnh nên không cần sổ trừ lùi riêng.
    /// </summary>
    private async Task RequireAdjustmentApprovalAsync(SessionContext ctx, Guid originalInvoiceId, int originalLineNo, AdjustmentKind kind, decimal totalAfter)
    {
        if (!await db.Companies.AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.RequireSalesAdjustmentApproval).FirstAsync()) return;
        var approved = (await (from r in db.AdjustmentRequests join l in db.AdjustmentRequestLines on r.Id equals l.RequestId
                               where r.CompanyId == ctx.CompanyId && r.InvoiceId == originalInvoiceId && r.Kind == kind && r.Status == ApprovalStatus.Approved && l.OriginalLineNo == originalLineNo
                               select kind == AdjustmentKind.Return ? l.Quantity : l.Amount).ToListAsync()).Sum(x => x ?? 0);
        if (totalAfter > approved)
            throw new DomainException(ErrorCodes.ApprovalRequired,
                $"Dòng {originalLineNo}: {(kind == AdjustmentKind.Return ? "trả hàng" : "giảm giá")} {totalAfter} vượt phạm vi đã được duyệt ({approved}); lập yêu cầu và chờ người có thẩm quyền duyệt (Bán hàng › Phê duyệt).");
    }
}
