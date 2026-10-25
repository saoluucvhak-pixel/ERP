using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.EInvoice;

public sealed record NoticeLineInput(Guid DocumentId, string ErrorDescription);

public sealed record NoticeLineView(Guid DocumentId, string? Series, string? Number, DateOnly IssueDate, string ErrorDescription);

public sealed record NoticeView(Guid Id, string Number, string Kind, string Status, string Reason, string? ProviderRef, string? Response, DateTime CreatedAt, DateTime? SentAt, DateTime? RespondedAt,
    long RowVersion, IReadOnlyList<NoticeLineView> Lines);

/// <summary>
/// Thông báo sai sót hóa đơn điện tử (Mẫu 04/SS-HĐĐT) [CẦN XÁC MINH mẫu]. Chị xác nhận 25/10: thông báo CHỈ ĐÍNH KÈM danh sách hóa đơn cần sửa, KHÔNG thay đổi hóa đơn
/// (trạng thái, số liệu) và không phát sinh bút toán. Lập (nháp) → gửi qua nhà cung cấp → cơ quan thuế tiếp nhận / không tiếp nhận; kết quả chỉ được ghi lại. Muốn đổi số liệu
/// thì lập hóa đơn điều chỉnh hoặc thay thế.
/// </summary>
public sealed class EInvoiceNoticeService(HakDbContext db, EInvoiceIssueService issue, IEInvoiceProvider provider, AuditService audit, IClock clock)
{
    public async Task<NoticeView> CreateAsync(SessionContext ctx, ErrorNoticeKind kind, string reason, IReadOnlyList<NoticeLineInput> lines)
    {
        ctx.Require(Permissions.EInvoiceNotice);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do.");
        if (lines.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Thông báo phải có ít nhất một hóa đơn.");
        if (lines.Select(l => l.DocumentId).Distinct().Count() != lines.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Một hóa đơn chỉ xuất hiện một lần trong thông báo.");
        var id = await db.InTransactionAsync(async () =>
        {
            var ids = lines.Select(l => l.DocumentId).ToList();
            var docs = await db.EInvoiceDocuments.AsNoTracking().Where(d => d.CompanyId == ctx.CompanyId && ids.Contains(d.Id)).ToListAsync();
            if (docs.Count != ids.Count) throw new DomainException(ErrorCodes.NotFound, "Có hóa đơn không tồn tại.");
            foreach (var d in docs.Where(d => d.Status != EInvoiceDocStatus.Issued))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {d.Series}/{d.Number} ở trạng thái {d.Status}; chỉ thông báo sai sót cho hóa đơn đã phát hành.");
            if (lines.Any(l => string.IsNullOrWhiteSpace(l.ErrorDescription))) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi hóa đơn phải mô tả sai sót.");
            if (await db.EInvoiceErrorNoticeLines.AnyAsync(l => ids.Contains(l.DocumentId) && db.EInvoiceErrorNotices.Any(n => n.Id == l.NoticeId && (n.Status == ErrorNoticeStatus.Draft || n.Status == ErrorNoticeStatus.Sent))))
                throw new DomainException(ErrorCodes.DuplicateSource, "Có hóa đơn đã nằm trong một thông báo đang chờ xử lý.");
            var date = DateOnly.FromDateTime(clock.UtcNow);
            var n = new EInvoiceErrorNotice
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Number = await Numbering.NextAsync(db, ctx.CompanyId, date, "TBSS"), Kind = kind, Reason = reason.Trim(),
                CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Lines = lines.Select(l => new EInvoiceErrorNoticeLine { Id = Ids.New(), CompanyId = ctx.CompanyId, DocumentId = l.DocumentId, ErrorDescription = l.ErrorDescription.Trim() }).ToList(),
            };
            foreach (var l in n.Lines) l.NoticeId = n.Id;
            db.EInvoiceErrorNotices.Add(n);
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.notice.create", "einvoice_notice", n.Id.ToString(), new { n.Number, kind, documents = ids.Count });
            await db.SaveChangesAsync();
            return n.Id;
        });
        return await ViewAsync(ctx, id);
    }

    /// <summary>Gửi thông báo qua nhà cung cấp. Lỗi tạm thời: giữ nguyên trạng thái Nháp để gửi lại (khóa chống trùng cố định theo thông báo).</summary>
    public async Task<NoticeView> SendAsync(SessionContext ctx, Guid noticeId, long expectedRowVersion)
    {
        ctx.Require(Permissions.EInvoiceNotice);
        if (!provider.Configured) throw new DomainException(ErrorCodes.ValidationFailed, "Chưa cấu hình nhà cung cấp hóa đơn điện tử (Hak:EInvoice:Provider).");
        var n = await db.EInvoiceErrorNotices.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == noticeId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thông báo không tồn tại.");
        RowVersionCheck.Require(n.RowVersion, expectedRowVersion, $"Thông báo {n.Number}");
        if (n.Status != ErrorNoticeStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thông báo {n.Number} ở trạng thái {n.Status}.");
        var docs = await db.EInvoiceDocuments.AsNoTracking().Where(d => n.Lines.Select(l => l.DocumentId).Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        var request = new ProviderNoticeRequest("TB-" + n.Id.ToString("N"), n.Kind, n.Reason,
            n.Lines.Select(l => new ProviderNoticeItem(docs[l.DocumentId].Series, docs[l.DocumentId].Number, docs[l.DocumentId].IssueDate, l.ErrorDescription)).ToList());
        ProviderNoticeResult result;
        try { result = await provider.SubmitNoticeAsync(request); }
        catch (EInvoiceTransientException ex)
        {
            await db.InTransactionAsync(async () =>
            {
                db.EInvoiceMessages.Add(new EInvoiceMessage { Id = Ids.New(), CompanyId = ctx.CompanyId, NoticeId = n.Id, Direction = "IN", Kind = "Transient", Code = "TRANSIENT", Detail = ex.Message, At = clock.UtcNow });
                await db.SaveChangesAsync();
            });
            throw new DomainException(ErrorCodes.ValidationFailed, $"Chưa gửi được thông báo {n.Number}: {ex.Message} Thông báo vẫn ở trạng thái nháp, gửi lại sau.");
        }
        await db.InTransactionAsync(async () =>
        {
            var cur = await db.EInvoiceErrorNotices.Include(x => x.Lines).FirstAsync(x => x.Id == noticeId);
            cur.Status = result.Accepted ? ErrorNoticeStatus.Accepted : ErrorNoticeStatus.Rejected; cur.ProviderRef = result.ProviderRef; cur.Response = result.Message;
            cur.SentAt = clock.UtcNow; cur.RespondedAt = clock.UtcNow; cur.RowVersion++;
            db.EInvoiceMessages.Add(new EInvoiceMessage { Id = Ids.New(), CompanyId = ctx.CompanyId, NoticeId = cur.Id, Direction = "IN", Kind = result.Accepted ? "NoticeAccepted" : "NoticeRejected", Code = result.ProviderRef, Detail = result.Message, At = clock.UtcNow });
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.notice.send", "einvoice_notice", cur.Id.ToString(), new { cur.Number, result.Accepted, result.Message }, cur.RowVersion);
            await db.SaveChangesAsync();
        });
        return await ViewAsync(ctx, noticeId);
    }

    public async Task<IReadOnlyList<NoticeView>> ListAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var ns = await db.EInvoiceErrorNotices.AsNoTracking().Include(n => n.Lines).Where(n => n.CompanyId == ctx.CompanyId).OrderByDescending(n => n.CreatedAt).Take(200).ToListAsync();
        return await ToViewsAsync(ns);
    }

    public async Task<NoticeView> ViewAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var n = await db.EInvoiceErrorNotices.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thông báo không tồn tại.");
        return (await ToViewsAsync([n]))[0];
    }

    private async Task<IReadOnlyList<NoticeView>> ToViewsAsync(List<EInvoiceErrorNotice> ns)
    {
        var ids = ns.SelectMany(n => n.Lines).Select(l => l.DocumentId).Distinct().ToList();
        var docs = await db.EInvoiceDocuments.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        return ns.Select(n => new NoticeView(n.Id, n.Number, n.Kind.ToString(), n.Status.ToString(), n.Reason, n.ProviderRef, n.Response, n.CreatedAt, n.SentAt, n.RespondedAt, n.RowVersion,
            n.Lines.Select(l => new NoticeLineView(l.DocumentId, docs[l.DocumentId].Series, docs[l.DocumentId].Number, docs[l.DocumentId].IssueDate, l.ErrorDescription)).ToList())).ToList();
    }
}
