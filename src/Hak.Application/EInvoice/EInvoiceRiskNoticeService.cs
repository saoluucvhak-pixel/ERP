using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.EInvoice;

public sealed record RiskNoticeLineInput(string Series, string InvoiceNo, DateOnly? IssueDate, string Issue);

public sealed record RiskNoticeLineView(string Series, string InvoiceNo, DateOnly? IssueDate, string Issue, Guid? DocumentId, bool MatchedInHak);

public sealed record RiskNoticeView(Guid Id, string Number, string AuthorityNoticeNo, DateOnly NoticeDate, DateOnly ReceivedDate, DateOnly? ResponseDeadline, string Reason, string Status,
    string? ResponseNote, Guid? ResponseNoticeId, string? ResponseNoticeNumber, DateTime? RespondedAt, bool Overdue, int? DaysLeft, long RowVersion, IReadOnlyList<RiskNoticeLineView> Lines);

/// <summary>
/// Thông báo 01/TB-RSĐT của cơ quan thuế (nhận vào). Ghi nhận → phản hồi (giải trình bằng văn bản và/hoặc gắn thông báo sai sót 04/SS đã lập) → đóng. Không đổi hóa đơn / sổ cái.
/// Số thông báo của cơ quan thuế duy nhất trong công ty; hạn phản hồi do người nhập; quá hạn chưa phản hồi được cảnh báo ở bảng kiểm đóng kỳ (mức "Cần xác nhận").
/// </summary>
public sealed class EInvoiceRiskNoticeService(HakDbContext db, AuditService audit, IClock clock)
{
    public async Task<RiskNoticeView> CreateAsync(SessionContext ctx, string authorityNoticeNo, DateOnly noticeDate, DateOnly receivedDate, DateOnly? responseDeadline, string reason, IReadOnlyList<RiskNoticeLineInput> lines)
    {
        ctx.Require(Permissions.EInvoiceNotice);
        if (string.IsNullOrWhiteSpace(authorityNoticeNo)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập số thông báo của cơ quan thuế.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập nội dung thông báo.");
        if (lines.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Thông báo phải nêu ít nhất một hóa đơn.");
        if (receivedDate < noticeDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày nhận không được trước ngày thông báo.");
        if (responseDeadline is { } dl && dl < receivedDate) throw new DomainException(ErrorCodes.ValidationFailed, "Hạn phản hồi không được trước ngày nhận.");
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.Series) || string.IsNullOrWhiteSpace(l.InvoiceNo) || string.IsNullOrWhiteSpace(l.Issue)))
            throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi hóa đơn phải có ký hiệu, số và nội dung rủi ro / sai sót.");
        var id = await db.InTransactionAsync(async () =>
        {
            var no = authorityNoticeNo.Trim();
            if (await db.EInvoiceRiskNotices.AnyAsync(n => n.CompanyId == ctx.CompanyId && n.AuthorityNoticeNo == no)) throw new DomainException(ErrorCodes.DuplicateSource, $"Thông báo {no} của cơ quan thuế đã được ghi nhận.");
            var docs = await db.EInvoiceDocuments.AsNoTracking().Where(d => d.CompanyId == ctx.CompanyId && d.Number != null).Select(d => new { d.Id, d.Series, d.Number }).ToListAsync();
            var n = new EInvoiceRiskNotice
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Number = await Numbering.NextAsync(db, ctx.CompanyId, receivedDate, "TBRS"), AuthorityNoticeNo = no, NoticeDate = noticeDate, ReceivedDate = receivedDate,
                ResponseDeadline = responseDeadline, Reason = reason.Trim(), CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1,
                Lines = lines.Select(l => new EInvoiceRiskNoticeLine
                {
                    Id = Ids.New(), CompanyId = ctx.CompanyId, Series = l.Series.Trim(), InvoiceNo = l.InvoiceNo.Trim(), IssueDate = l.IssueDate, Issue = l.Issue.Trim(),
                    DocumentId = docs.FirstOrDefault(d => d.Series == l.Series.Trim() && d.Number == l.InvoiceNo.Trim())?.Id,
                }).ToList(),
            };
            foreach (var l in n.Lines) l.NoticeId = n.Id;
            db.EInvoiceRiskNotices.Add(n);
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.risk_notice.create", "einvoice_risk_notice", n.Id.ToString(), new { n.Number, no, invoices = lines.Count, responseDeadline });
            await db.SaveChangesAsync();
            return n.Id;
        });
        return await ViewAsync(ctx, id);
    }

    /// <summary>Phản hồi: bắt buộc có nội dung giải trình hoặc gắn một thông báo sai sót 04/SS đã lập; không đổi hóa đơn.</summary>
    public async Task<RiskNoticeView> RespondAsync(SessionContext ctx, Guid id, long expectedRowVersion, string? note, Guid? responseNoticeId)
    {
        ctx.Require(Permissions.EInvoiceNotice);
        if (string.IsNullOrWhiteSpace(note) && responseNoticeId is null) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập nội dung phản hồi hoặc chọn thông báo sai sót 04/SS đã lập.");
        await db.InTransactionAsync(async () =>
        {
            var n = await db.EInvoiceRiskNotices.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thông báo không tồn tại.");
            RowVersionCheck.Require(n.RowVersion, expectedRowVersion, $"Thông báo {n.Number}");
            if (n.Status != RiskNoticeStatus.Received) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Thông báo {n.Number} đã được phản hồi.");
            if (responseNoticeId is { } rid && !await db.EInvoiceErrorNotices.AnyAsync(x => x.Id == rid && x.CompanyId == ctx.CompanyId)) throw new DomainException(ErrorCodes.NotFound, "Thông báo sai sót 04/SS không tồn tại.");
            n.ResponseNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(); n.ResponseNoticeId = responseNoticeId;
            n.Status = RiskNoticeStatus.Responded; n.RespondedAt = clock.UtcNow; n.RespondedBy = ctx.UserId; n.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.risk_notice.respond", "einvoice_risk_notice", n.Id.ToString(), new { n.Number, responseNoticeId }, n.RowVersion);
            await db.SaveChangesAsync();
        });
        return await ViewAsync(ctx, id);
    }

    public async Task<RiskNoticeView> CloseAsync(SessionContext ctx, Guid id, long expectedRowVersion)
    {
        ctx.Require(Permissions.EInvoiceNotice);
        await db.InTransactionAsync(async () =>
        {
            var n = await db.EInvoiceRiskNotices.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thông báo không tồn tại.");
            RowVersionCheck.Require(n.RowVersion, expectedRowVersion, $"Thông báo {n.Number}");
            if (n.Status != RiskNoticeStatus.Responded) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ đóng thông báo đã phản hồi.");
            n.Status = RiskNoticeStatus.Closed; n.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.risk_notice.close", "einvoice_risk_notice", n.Id.ToString(), new { n.Number }, n.RowVersion);
            await db.SaveChangesAsync();
        });
        return await ViewAsync(ctx, id);
    }

    public async Task<IReadOnlyList<RiskNoticeView>> ListAsync(SessionContext ctx, DateOnly? today = null)
    {
        ctx.Require(Permissions.ReportView);
        var ns = await db.EInvoiceRiskNotices.AsNoTracking().Include(n => n.Lines).Where(n => n.CompanyId == ctx.CompanyId).OrderBy(n => n.Status).ThenBy(n => n.ResponseDeadline).ThenByDescending(n => n.ReceivedDate).Take(300).ToListAsync();
        return await ToViewsAsync(ns, today ?? DateOnly.FromDateTime(clock.UtcNow));
    }

    public async Task<RiskNoticeView> ViewAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var n = await db.EInvoiceRiskNotices.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Thông báo không tồn tại.");
        return (await ToViewsAsync([n], DateOnly.FromDateTime(clock.UtcNow)))[0];
    }

    private async Task<IReadOnlyList<RiskNoticeView>> ToViewsAsync(IReadOnlyList<EInvoiceRiskNotice> ns, DateOnly today)
    {
        var rids = ns.Where(n => n.ResponseNoticeId != null).Select(n => n.ResponseNoticeId!.Value).ToList();
        var numbers = await db.EInvoiceErrorNotices.AsNoTracking().Where(x => rids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number);
        return ns.Select(n =>
        {
            var pending = n.Status == RiskNoticeStatus.Received;
            int? left = n.ResponseDeadline is { } d ? d.DayNumber - today.DayNumber : null;
            return new RiskNoticeView(n.Id, n.Number, n.AuthorityNoticeNo, n.NoticeDate, n.ReceivedDate, n.ResponseDeadline, n.Reason, n.Status.ToString(), n.ResponseNote, n.ResponseNoticeId,
                n.ResponseNoticeId is { } r && numbers.TryGetValue(r, out var num) ? num : null, n.RespondedAt, pending && left is < 0, pending ? left : null, n.RowVersion,
                n.Lines.Select(l => new RiskNoticeLineView(l.Series, l.InvoiceNo, l.IssueDate, l.Issue, l.DocumentId, l.DocumentId != null)).ToList());
        }).ToList();
    }
}
