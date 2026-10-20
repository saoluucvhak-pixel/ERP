using System.Security.Cryptography;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Attachments;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Attachments;

/// <summary>Nơi lưu nội dung tệp đính kèm trên máy chủ.</summary>
public sealed class AttachmentStore(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public async Task WriteAsync(string relative, byte[] data)
    {
        var path = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data);
    }

    public Task<byte[]> ReadAsync(string relative) => File.ReadAllBytesAsync(Resolve(relative));

    public void Delete(string relative) { try { File.Delete(Resolve(relative)); } catch (IOException) { /* dọn dẹp sau lỗi: bỏ qua */ } }

    /// <summary>Chỉ nhận đường dẫn tương đối nằm trong thư mục gốc (chặn ../).</summary>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new DomainException(ErrorCodes.ValidationFailed, "Đường dẫn tệp không hợp lệ.");
        return full;
    }
}

public sealed record AttachmentView(Guid Id, string EntityType, Guid EntityId, string FileName, string ContentType, long SizeBytes, string? Caption, DateTime UploadedAt, Guid UploadedBy);

/// <summary>
/// Ảnh / tệp đính kèm cho phiếu yêu cầu sửa chữa (báo sự cố) và phiếu thi công. Chỉ nhận ảnh JPEG, PNG, WebP kiểm tra theo chữ ký đầu tệp (không tin Content-Type
/// hay đuôi tệp), tối đa 8 MB và 12 ảnh mỗi phiếu. Có nhật ký kiểm toán; không xóa.
/// </summary>
public sealed class AttachmentService(HakDbContext db, AttachmentStore store, AuditService audit, IClock clock)
{
    public const long MaxBytes = 8 * 1024 * 1024;
    public const int MaxPerEntity = 12;
    private static readonly string[] EntityTypes = ["maintenance_request", "asset_repair"];

    /// <summary>Loại ảnh theo chữ ký đầu tệp; null nếu không phải JPEG / PNG / WebP.</summary>
    public static (string ContentType, string Ext)? Sniff(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A) return ("image/png", ".png");
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return ("image/webp", ".webp");
        return null;
    }

    private static string CleanName(string? name)
    {
        var n = Path.GetFileName((name ?? "").Replace('\\', '/'));
        n = new string(n.Where(c => !char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '|' or '?' or '*')).ToArray()).Trim();
        return string.IsNullOrEmpty(n) ? "anh" : n.Length > 120 ? n[^120..] : n;
    }

    public async Task<FileAttachment> AddAsync(SessionContext ctx, string entityType, Guid entityId, string? fileName, byte[] data, string? caption)
    {
        ctx.Require(Permissions.AssetManage);
        if (!EntityTypes.Contains(entityType)) throw new DomainException(ErrorCodes.ValidationFailed, "Loại đối tượng đính kèm không hợp lệ.");
        if (data.Length == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Tệp rỗng.");
        if (data.Length > MaxBytes) throw new DomainException(ErrorCodes.ValidationFailed, $"Ảnh lớn hơn {MaxBytes / 1024 / 1024} MB; chụp lại ở độ phân giải thấp hơn.");
        var kind = Sniff(data) ?? throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ nhận ảnh JPEG, PNG hoặc WebP.");
        FileAttachment? added = null;
        try
        {
            return await db.InTransactionAsync(async () =>
            {
                var exists = entityType == "maintenance_request"
                    ? await db.MaintenanceRequests.AnyAsync(r => r.Id == entityId && r.CompanyId == ctx.CompanyId)
                    : await db.AssetRepairs.AnyAsync(r => r.Id == entityId && r.CompanyId == ctx.CompanyId);
                if (!exists) throw new DomainException(ErrorCodes.NotFound, "Phiếu không tồn tại.");
                if (await db.FileAttachments.CountAsync(a => a.CompanyId == ctx.CompanyId && a.EntityType == entityType && a.EntityId == entityId) >= MaxPerEntity)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"Mỗi phiếu tối đa {MaxPerEntity} ảnh.");
                var id = Ids.New();
                var now = clock.UtcNow;
                var relative = Path.Combine(ctx.CompanyId.ToString("N"), now.ToString("yyyy"), now.ToString("MM"), id.ToString("N") + kind.Ext);
                await store.WriteAsync(relative, data);
                var a = added = new FileAttachment { Id = id, CompanyId = ctx.CompanyId, EntityType = entityType, EntityId = entityId, FileName = CleanName(fileName), ContentType = kind.ContentType,
                    SizeBytes = data.Length, Sha256 = Convert.ToHexString(SHA256.HashData(data)), StoragePath = relative, Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(), UploadedAt = now, UploadedBy = ctx.UserId };
                db.FileAttachments.Add(a);
                await audit.AppendAsync(ctx.CompanyId, ctx, "attachment.add", entityType, entityId.ToString(), new { a.Id, a.FileName, a.SizeBytes, a.Sha256 });
                await db.SaveChangesAsync();
                return a;
            });
        }
        catch { if (added is not null) store.Delete(added.StoragePath); throw; }   // giao dịch lỗi thì không để tệp mồ côi
    }

    public async Task<IReadOnlyList<AttachmentView>> ListAsync(SessionContext ctx, string entityType, Guid entityId)
    {
        ctx.Require(Permissions.ReportView);
        var rows = await db.FileAttachments.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.EntityType == entityType && a.EntityId == entityId).OrderBy(a => a.UploadedAt).ToListAsync();
        return rows.Select(a => new AttachmentView(a.Id, a.EntityType, a.EntityId, a.FileName, a.ContentType, a.SizeBytes, a.Caption, a.UploadedAt, a.UploadedBy)).ToList();
    }

    /// <summary>Nội dung tệp; kiểm tra lại SHA-256 để phát hiện tệp bị sửa ngoài phần mềm.</summary>
    public async Task<(FileAttachment Meta, byte[] Data)> ReadAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var a = await db.FileAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Tệp không tồn tại.");
        byte[] data;
        try { data = await store.ReadAsync(a.StoragePath); }
        catch (FileNotFoundException) { throw new DomainException(ErrorCodes.NotFound, "Tệp đính kèm không còn trên máy chủ."); }
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(data)), a.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(ErrorCodes.IntegrityViolation, "Tệp đính kèm đã bị thay đổi ngoài phần mềm (sai mã băm).");
        return (a, data);
    }

    /// <summary>Số ảnh của từng phiếu, để hiện biểu tượng trong danh sách.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> CountsAsync(SessionContext ctx, string entityType)
    {
        ctx.Require(Permissions.ReportView);
        var rows = await db.FileAttachments.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.EntityType == entityType).GroupBy(a => a.EntityId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        return rows.ToDictionary(r => r.Key, r => r.N);
    }
}
