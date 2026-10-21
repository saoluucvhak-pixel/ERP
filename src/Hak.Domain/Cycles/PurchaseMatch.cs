namespace Hak.Domain.Cycles;

public enum MatchPolicyScope { Company = 1, Partner = 2 }

/// <summary>
/// Chính sách dung sai đối chiếu hóa đơn mua (Đợt 3). Có phiên bản: đổi chính sách tạo phiên bản mới và đóng phiên bản cũ; phiên bản đang hiệu lực của nhà cung cấp
/// được ưu tiên hơn của công ty. Dung sai giá (tuyệt đối hoặc %) tách khỏi dung sai lượng (%); không có chính sách thì dung sai bằng 0.
/// </summary>
public sealed class PurchaseMatchPolicy
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public MatchPolicyScope Scope { get; set; }
    /// <summary>Nhà cung cấp áp dụng khi <see cref="Scope"/> là Partner.</summary>
    public Guid? PartnerId { get; set; }
    public int Version { get; set; }
    /// <summary>Dung sai đơn giá theo % của đơn giá đơn đặt hàng.</summary>
    public decimal PricePercent { get; set; }
    /// <summary>Dung sai đơn giá tuyệt đối (đồng trên một đơn vị tính giá). Lấy số lớn hơn giữa hai dung sai giá.</summary>
    public decimal PriceAbsolute { get; set; }
    /// <summary>Dung sai lượng hóa đơn lũy kế so với lượng đặt, theo %.</summary>
    public decimal QuantityPercent { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum VarianceApprovalStatus { Active = 1, Superseded = 2, Revoked = 3 }

/// <summary>
/// Chốt duyệt sai lệch đối chiếu của một hóa đơn mua: lưu người duyệt, lý do, ảnh chụp dữ liệu đối chiếu (băm + nội dung) và phiên bản chính sách tại thời điểm duyệt.
/// Hiệu lực chỉ khi dữ liệu đối chiếu hiện tại có cùng mã băm; lượng, giá hoặc kết quả kiểm nghiệm nguồn đổi thì phải duyệt lại.
/// </summary>
public sealed class PurchaseVarianceApproval
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public string SnapshotHash { get; set; } = "";
    public string SnapshotText { get; set; } = "";
    public int? PolicyVersion { get; set; }
    public string Reason { get; set; } = "";
    public VarianceApprovalStatus Status { get; set; } = VarianceApprovalStatus.Active;
    public Guid ApprovedBy { get; set; }
    public DateTime ApprovedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
}
