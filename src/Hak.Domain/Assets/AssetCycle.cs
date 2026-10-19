using Hak.Domain.Common;

namespace Hak.Domain.Assets;

public enum AssetRequestStatus { Pending = 1, Approved = 2, Rejected = 3, Fulfilled = 4, Cancelled = 9 }

/// <summary>
/// Đề nghị mua sắm / đầu tư TSCĐ, CCDC (tệp phân tích ERP, mục IV.2: chu trình tài chính gồm đầu tư xây dựng, mua sắm tài sản cố định). Không ghi Nhật ký.
/// Được duyệt rồi thì mua qua chu trình cung ứng (hóa đơn mua), sau đó lập thẻ tài sản và gắn vào đề nghị (Fulfilled).
/// </summary>
public sealed class AssetRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>DNMS{năm}-{số}.</summary>
    public string Number { get; set; } = "";
    public AssetClass Class { get; set; }
    public string Name { get; set; } = "";
    public string? Department { get; set; }
    public decimal EstimatedCost { get; set; }
    public DateOnly NeededDate { get; set; }
    public string Reason { get; set; } = "";
    public AssetRequestStatus Status { get; set; } = AssetRequestStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? AssetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }

    public void Decide(bool approve, Guid userId, DateTime at, string? note, bool requireDistinct)
    {
        if (Status != AssetRequestStatus.Pending) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đề nghị {Number} không còn chờ duyệt.");
        if (requireDistinct && userId == CreatedBy) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt đề nghị mua sắm không được trùng người lập.");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối đề nghị phải có lý do.");
        Status = approve ? AssetRequestStatus.Approved : AssetRequestStatus.Rejected;
        DecidedBy = userId; DecidedAt = at; DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

/// <summary>Bàn giao tài sản cho bộ phận / người sử dụng. Lần bàn giao mới đóng lần trước (EndedDate). Không ghi Nhật ký.</summary>
public sealed class AssetAssignment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public string Department { get; set; } = "";
    public string? Custodian { get; set; }
    public string? Location { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum AssetCountStatus { Draft = 1, Completed = 2, Cancelled = 9 }

/// <summary>Kiểm kê tài sản định kỳ hoặc đột xuất (tệp mục III.2 kiểm soát phát hiện). Không ghi Nhật ký; chênh lệch chuyển sang ghi giảm / điều tra.</summary>
public sealed class AssetCount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>KKTS{năm}-{số}.</summary>
    public string Number { get; set; } = "";
    public DateOnly CountDate { get; set; }
    public string? Note { get; set; }
    public AssetCountStatus Status { get; set; } = AssetCountStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long RowVersion { get; set; }
    public List<AssetCountLine> Lines { get; set; } = [];
}

public enum AssetCountResult { NotCounted = 0, Found = 1, Missing = 2, Damaged = 3 }

public sealed class AssetCountLine
{
    public Guid Id { get; set; }
    public Guid CountId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    /// <summary>Bộ phận theo sổ tại ngày kiểm kê.</summary>
    public string? BookDepartment { get; set; }
    public AssetCountResult Result { get; set; } = AssetCountResult.NotCounted;
    /// <summary>Bộ phận / vị trí thực tế khi khác sổ.</summary>
    public string? ActualDepartment { get; set; }
    public string? Note { get; set; }
}
