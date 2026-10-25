using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Hak.Domain.Common;
using Hak.Domain.Tax;

namespace Hak.Application.EInvoice;

public sealed record ProviderParty(string Name, string? TaxCode, string? Address);

public sealed record ProviderLine(int LineNo, string Name, string? Unit, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Amount, decimal? VatRate, decimal VatAmount);

/// <summary>Yêu cầu phát hành gửi nhà cung cấp. <see cref="IdempotencyKey"/> giống nhau ⇒ cùng một hóa đơn (nhà cung cấp không được phát hành hai lần).</summary>
public sealed record ProviderIssueRequest(string IdempotencyKey, string TemplateCode, DateOnly IssueDate, EInvoiceRelation Relation, EInvoiceAdjustNature Nature, string? Reason,
    string? RelatedTemplate, string? RelatedSeries, string? RelatedNumber, DateOnly? RelatedDate, ProviderParty Seller, ProviderParty Buyer, string Currency, decimal? ExchangeRate,
    IReadOnlyList<ProviderLine> Lines, decimal AmountBeforeVat, decimal VatAmount, decimal Total, string XmlDraft);

public enum ProviderOutcome { Accepted = 1, Rejected = 2 }

/// <summary>Kết quả cuối. Chỉ Accepted mới có số, ký hiệu, ngày ký, mã cơ quan thuế và XML đã ký.</summary>
public sealed record ProviderIssueResult(ProviderOutcome Outcome, string? ProviderRef, string? Series, string? Number, DateTime? SignedAt, string? TaxAuthorityCode, string? Xml,
    string? ErrorCode, string? ErrorMessage);

public sealed record ProviderNoticeItem(string? Series, string? Number, DateOnly IssueDate, string Description);

public sealed record ProviderNoticeRequest(string IdempotencyKey, ErrorNoticeKind Kind, string Reason, IReadOnlyList<ProviderNoticeItem> Items);

public sealed record ProviderNoticeResult(bool Accepted, string? ProviderRef, string? Message);

/// <summary>Lỗi tạm thời (mạng, quá tải, hết giờ): chưa biết nhà cung cấp đã nhận hay chưa — gửi lại với CÙNG khóa chống trùng.</summary>
public sealed class EInvoiceTransientException(string message) : Exception(message);

/// <summary>
/// Cổng tới nhà cung cấp hóa đơn điện tử / T-VAN. Mỗi nhà cung cấp một bộ chuyển đổi (adapter); lõi ERP chỉ biết giao diện này.
/// Chữ ký số nằm ở phía nhà cung cấp (HSM / ký từ xa): ERP không giữ khóa ký.
/// </summary>
public interface IEInvoiceProvider
{
    string Name { get; }
    bool Configured { get; }
    Task<ProviderIssueResult> IssueAsync(ProviderIssueRequest request, CancellationToken ct = default);
    Task<ProviderNoticeResult> SubmitNoticeAsync(ProviderNoticeRequest request, CancellationToken ct = default);
}

/// <summary>Mặc định an toàn: chưa chọn nhà cung cấp thì không phát hành được (không bao giờ giả lập ngầm trên dữ liệu thật).</summary>
public sealed class NullEInvoiceProvider : IEInvoiceProvider
{
    public string Name => "NONE";
    public bool Configured => false;
    public Task<ProviderIssueResult> IssueAsync(ProviderIssueRequest request, CancellationToken ct = default)
        => throw new DomainException(ErrorCodes.ValidationFailed, "Chưa cấu hình nhà cung cấp hóa đơn điện tử (Hak:EInvoice:Provider).");
    public Task<ProviderNoticeResult> SubmitNoticeAsync(ProviderNoticeRequest request, CancellationToken ct = default)
        => throw new DomainException(ErrorCodes.ValidationFailed, "Chưa cấu hình nhà cung cấp hóa đơn điện tử (Hak:EInvoice:Provider).");
}

/// <summary>
/// Nhà cung cấp GIẢ LẬP để thử toàn luồng (kiểm thử, môi trường Development). Không ký số thật, mã cơ quan thuế là chuỗi băm giả — tuyệt đối không dùng cho hóa đơn thật.
/// Chống trùng theo khóa như nhà cung cấp thật: gửi lại cùng khóa trả đúng kết quả cũ. MST người mua có nhưng không đủ 10 hoặc 13 chữ số ⇒ từ chối.
/// </summary>
public sealed class SimulatedEInvoiceProvider : IEInvoiceProvider
{
    private readonly ConcurrentDictionary<string, ProviderIssueResult> issued = new();
    private readonly ConcurrentDictionary<string, ProviderNoticeResult> notices = new();
    private readonly Lock gate = new();
    private int counter;
    /// <summary>Số lần gọi tới sẽ ném lỗi tạm thời (để kiểm thử gửi lại).</summary>
    public int FailTransientCalls { get; set; }
    /// <summary>Số lần nhận thật sự (không tính gửi lại trùng khóa).</summary>
    public int AcceptedCount => issued.Values.Count(r => r.Outcome == ProviderOutcome.Accepted);
    public bool RejectNotices { get; set; }
    public string Name => "SIMULATED";
    public bool Configured => true;

    private void MaybeFail()
    {
        lock (gate) { if (FailTransientCalls > 0) { FailTransientCalls--; throw new EInvoiceTransientException("Mất kết nối tới nhà cung cấp (giả lập)."); } }
    }

    public Task<ProviderIssueResult> IssueAsync(ProviderIssueRequest r, CancellationToken ct = default)
    {
        MaybeFail();
        return Task.FromResult(issued.GetOrAdd(r.IdempotencyKey, _ => Decide(r)));
    }

    private ProviderIssueResult Decide(ProviderIssueRequest r)
    {
        var tax = r.Buyer.TaxCode?.Trim();
        if (!string.IsNullOrEmpty(tax) && (!tax.All(char.IsDigit) || tax.Length is not (10 or 13)))
            return new(ProviderOutcome.Rejected, null, null, null, null, null, null, "MST_NGUOI_MUA", $"Mã số thuế người mua '{tax}' không hợp lệ (10 hoặc 13 chữ số).");
        if (r.Total <= 0 && r.Nature != EInvoiceAdjustNature.Information)
            return new(ProviderOutcome.Rejected, null, null, null, null, null, null, "TONG_TIEN", "Tổng tiền hóa đơn phải lớn hơn 0.");
        int n;
        lock (gate) n = ++counter;
        var series = "C" + r.IssueDate.ToString("yy") + "TSM";
        var number = n.ToString("D7");
        var signedAt = DateTime.SpecifyKind(r.IssueDate.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Utc);
        var code = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{series}/{number}/{r.IdempotencyKey}")))[..34];
        var xml = r.XmlDraft.Replace("<KyHieu />", $"<KyHieu>{series}</KyHieu>").Replace("<SoHoaDon />", $"<SoHoaDon>{number}</SoHoaDon>")
            .Replace("<MaCQT />", $"<MaCQT>{code}</MaCQT>").Replace("<NgayKy />", $"<NgayKy>{signedAt:yyyy-MM-ddTHH:mm:ssZ}</NgayKy>");
        return new(ProviderOutcome.Accepted, "SIM-" + n.ToString("D7"), series, number, signedAt, code, xml, null, null);
    }

    public Task<ProviderNoticeResult> SubmitNoticeAsync(ProviderNoticeRequest r, CancellationToken ct = default)
    {
        MaybeFail();
        return Task.FromResult(notices.GetOrAdd(r.IdempotencyKey, _ => RejectNotices
            ? new ProviderNoticeResult(false, null, "Cơ quan thuế không tiếp nhận thông báo (giả lập).")
            : new ProviderNoticeResult(true, "SIM-TB-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.IdempotencyKey)))[..8], "Đã tiếp nhận (giả lập).")));
    }
}
