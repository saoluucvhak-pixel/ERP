using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record MatchPolicyInput(MatchPolicyScope Scope, Guid? PartnerId, decimal PricePercent, decimal PriceAbsolute, decimal QuantityPercent, DateOnly EffectiveFrom, string? Note = null);
public sealed record MatchPolicyView(Guid Id, string Scope, Guid? PartnerId, string? PartnerName, int Version, decimal PricePercent, decimal PriceAbsolute, decimal QuantityPercent,
    DateOnly EffectiveFrom, bool IsActive, string? Note, DateTime CreatedAt);
public sealed record VarianceApprovalView(Guid Id, Guid InvoiceId, string Status, string Reason, int? PolicyVersion, Guid ApprovedBy, DateTime ApprovedAt, string SnapshotHash,
    bool StillValid, string? CloseReason);

/// <summary>
/// Duyệt sai lệch đối chiếu hóa đơn mua (Đợt 3): quyền riêng (payment.variance) tách khỏi chấp nhận thanh toán; lưu người, lý do, phiên bản chính sách và ảnh chụp dữ liệu đối chiếu.
/// Chốt duyệt chỉ còn hiệu lực khi dữ liệu đối chiếu hiện tại có cùng mã băm: lượng nhận / đạt, hóa đơn khác, đơn giá hoặc chính sách đổi thì phải duyệt lại.
/// Cũng quản lý chính sách dung sai có phiên bản (công ty hoặc nhà cung cấp).
/// </summary>
public sealed class PurchaseVarianceService(HakDbContext db, AuditService audit, IClock clock)
{
    public Task<VarianceApprovalView> ApproveAsync(SessionContext ctx, Guid invoiceId, string reason)
    {
        ctx.Require(Permissions.PaymentVariance);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do duyệt sai lệch.");
        return db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (inv.Kind != InvoiceKind.Purchase || inv.Purpose != InvoicePurpose.Normal || inv.Status != InvoiceStatus.Posted)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ duyệt sai lệch cho hóa đơn mua thường đã ghi sổ.");
            var match = await new PurchaseMatchService(db).MatchCoreAsync(ctx.CompanyId, invoiceId);
            if (match.BlockingCount == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn không có sai lệch cần duyệt.");
            var a = await CreateAsync(ctx, inv, match, reason.Trim());
            await db.SaveChangesAsync();
            return View(a, match.SnapshotHash);
        });
    }

    /// <summary>Tạo chốt duyệt trong transaction hiện tại (đóng các chốt Active cũ của hóa đơn). Dùng cả khi chấp nhận thanh toán kèm xác nhận.</summary>
    internal async Task<PurchaseVarianceApproval> CreateAsync(SessionContext ctx, CommercialInvoice inv, PurchaseMatch match, string reason)
    {
        foreach (var old in await db.PurchaseVarianceApprovals.Where(x => x.CompanyId == ctx.CompanyId && x.InvoiceId == inv.Id && x.Status == VarianceApprovalStatus.Active).ToListAsync())
        {
            old.Status = VarianceApprovalStatus.Superseded; old.ClosedBy = ctx.UserId; old.ClosedAt = clock.UtcNow; old.CloseReason = "Có chốt duyệt mới";
        }
        var a = new PurchaseVarianceApproval
        {
            Id = Ids.New(), CompanyId = ctx.CompanyId, InvoiceId = inv.Id, SnapshotHash = match.SnapshotHash, SnapshotText = match.SnapshotText, PolicyVersion = match.PolicyVersion,
            Reason = reason, ApprovedBy = ctx.UserId, ApprovedAt = clock.UtcNow, Status = VarianceApprovalStatus.Active,
        };
        db.PurchaseVarianceApprovals.Add(a);
        await audit.AppendAsync(ctx.CompanyId, ctx, "purchase_variance.approve", "invoice", inv.Id.ToString(),
            new { inv.Number, match.SnapshotHash, match.PolicyVersion, reason, blocking = match.Lines.SelectMany(l => l.Flags.Where(f => f.Blocking).Select(f => $"Dòng {l.LineNo}: {f.Message}")) });
        return a;
    }

    public Task RevokeAsync(SessionContext ctx, Guid approvalId, string reason)
    {
        ctx.Require(Permissions.PaymentVariance);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do thu hồi.");
        return db.InTransactionAsync(async () =>
        {
            var a = await db.PurchaseVarianceApprovals.FirstOrDefaultAsync(x => x.Id == approvalId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Không có chốt duyệt này.");
            if (a.Status != VarianceApprovalStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chốt duyệt đã đóng.");
            a.Status = VarianceApprovalStatus.Revoked; a.ClosedBy = ctx.UserId; a.ClosedAt = clock.UtcNow; a.CloseReason = reason.Trim();
            await audit.AppendAsync(ctx.CompanyId, ctx, "purchase_variance.revoke", "invoice", a.InvoiceId.ToString(), new { approval = a.Id, reason });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Chốt duyệt Active của hóa đơn kèm cờ còn hiệu lực với dữ liệu đối chiếu hiện tại.</summary>
    public async Task<VarianceApprovalView?> CurrentAsync(SessionContext ctx, Guid invoiceId)
    {
        ctx.Require(Permissions.ReportView);
        var a = await db.PurchaseVarianceApprovals.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId && x.InvoiceId == invoiceId && x.Status == VarianceApprovalStatus.Active)
            .OrderByDescending(x => x.ApprovedAt).FirstOrDefaultAsync();
        if (a is null) return null;
        var match = await new PurchaseMatchService(db).MatchCoreAsync(ctx.CompanyId, invoiceId);
        return View(a, match.SnapshotHash);
    }

    /// <summary>Hóa đơn đang có sai lệch chặn và có chốt duyệt Active trùng mã băm hiện tại.</summary>
    internal static async Task<bool> HasValidApprovalAsync(HakDbContext db, Guid companyId, Guid invoiceId, string currentHash)
        => await db.PurchaseVarianceApprovals.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.InvoiceId == invoiceId && x.Status == VarianceApprovalStatus.Active && x.SnapshotHash == currentHash);

    private static VarianceApprovalView View(PurchaseVarianceApproval a, string currentHash)
        => new(a.Id, a.InvoiceId, a.Status.ToString(), a.Reason, a.PolicyVersion, a.ApprovedBy, a.ApprovedAt, a.SnapshotHash, a.Status == VarianceApprovalStatus.Active && a.SnapshotHash == currentHash, a.CloseReason);

    // ---------------------------------------------------------------- chính sách dung sai (có phiên bản)

    public Task<MatchPolicyView> SetPolicyAsync(SessionContext ctx, MatchPolicyInput input)
    {
        ctx.Require(Permissions.CatalogManage);
        if (input.PricePercent < 0 || input.PriceAbsolute < 0 || input.QuantityPercent < 0 || input.PricePercent > 100 || input.QuantityPercent > 100
            || !MoneyMath.HasAtMostDecimals(input.PricePercent, 4) || !MoneyMath.HasAtMostDecimals(input.PriceAbsolute, 4) || !MoneyMath.HasAtMostDecimals(input.QuantityPercent, 4))
            throw new DomainException(ErrorCodes.ValidationFailed, "Dung sai phải từ 0 (phần trăm tối đa 100), tối đa 4 chữ số thập phân.");
        if (input.Scope == MatchPolicyScope.Partner != (input.PartnerId is not null))
            throw new DomainException(ErrorCodes.ValidationFailed, input.Scope == MatchPolicyScope.Partner ? "Chính sách theo nhà cung cấp phải chọn nhà cung cấp." : "Chính sách công ty không gắn nhà cung cấp.");
        return db.InTransactionAsync(async () =>
        {
            if (input.PartnerId is { } pid && !await db.BusinessPartners.AnyAsync(p => p.Id == pid && p.CompanyId == ctx.CompanyId))
                throw new DomainException(ErrorCodes.NotFound, "Nhà cung cấp không tồn tại.");
            var same = await db.PurchaseMatchPolicies.Where(p => p.CompanyId == ctx.CompanyId && p.Scope == input.Scope && p.PartnerId == input.PartnerId).ToListAsync();
            foreach (var old in same.Where(p => p.IsActive)) old.IsActive = false;
            var p = new PurchaseMatchPolicy
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Scope = input.Scope, PartnerId = input.PartnerId, Version = (same.Count == 0 ? 0 : same.Max(x => x.Version)) + 1,
                PricePercent = input.PricePercent, PriceAbsolute = input.PriceAbsolute, QuantityPercent = input.QuantityPercent, EffectiveFrom = input.EffectiveFrom,
                IsActive = true, Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.PurchaseMatchPolicies.Add(p);
            await audit.AppendAsync(ctx.CompanyId, ctx, "purchase_match_policy.set", "purchase_match_policy", p.Id.ToString(),
                new { p.Scope, p.PartnerId, p.Version, p.PricePercent, p.PriceAbsolute, p.QuantityPercent, p.EffectiveFrom });
            await db.SaveChangesAsync();
            return ToView(p, null);
        });
    }

    public async Task<IReadOnlyList<MatchPolicyView>> ListPoliciesAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var rows = await db.PurchaseMatchPolicies.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Name);
        return rows.OrderBy(p => p.Scope).ThenBy(p => p.PartnerId).ThenByDescending(p => p.Version).Select(p => ToView(p, p.PartnerId is { } pid ? partners.GetValueOrDefault(pid) : null)).ToList();
    }

    private static MatchPolicyView ToView(PurchaseMatchPolicy p, string? partnerName)
        => new(p.Id, p.Scope.ToString(), p.PartnerId, partnerName, p.Version, p.PricePercent, p.PriceAbsolute, p.QuantityPercent, p.EffectiveFrom, p.IsActive, p.Note, p.CreatedAt);
}
