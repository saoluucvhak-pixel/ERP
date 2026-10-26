using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Tax;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.EInvoice;

/// <summary>Cấu hình phát hành theo công ty. Ký hiệu mẫu số do doanh nghiệp đăng ký với cơ quan thuế [CẦN XÁC MINH]; mặc định "1" chỉ là giá trị khởi tạo.</summary>
public sealed record EInvoiceOptions(string TemplateCode = "1");

public sealed record EInvoiceDocumentView(Guid Id, Guid? InvoiceId, string? InvoiceNumber, string Status, string Relation, string Nature, string? Series, string? Number, DateOnly IssueDate,
    string? TaxAuthorityCode, string? Reason, string? LastError, string? PostError, int Attempts, string? OutboxState, DateTime? NextAttemptAt, long RowVersion, string? PartnerName,
    decimal? Total, Guid? RelatedDocumentId, string? RelatedNumber, string Provider);

public sealed record ReplacementLineChange(int LineNo, decimal? UnitPrice = null, decimal? VatRate = null, decimal? DiscountPercent = null);

public sealed record InformationChange(string Field, string OldValue, string NewValue);

public sealed record ProcessResult(int Dispatched, int Issued, int Rejected, int Pending);

/// <summary>
/// Phát hành hóa đơn điện tử từ ERP (docs/12). Luồng: hóa đơn nháp → lệnh phát hành vào hàng đợi (cùng giao dịch với trạng thái Issuing) → gửi nhà cung cấp NGOÀI giao dịch cơ sở dữ liệu
/// → kết quả: Accepted ⇒ lưu số / ký hiệu / mã cơ quan thuế / XML (giao dịch riêng, không bao giờ mất) rồi ghi sổ hóa đơn ERP bằng số thật; Rejected ⇒ giữ hóa đơn nháp, báo lỗi;
/// lỗi tạm thời ⇒ ở lại hàng đợi, gửi lại với CÙNG khóa chống trùng. Hóa đơn thay thế đảo hóa đơn gốc trong cùng giao dịch ghi sổ; điều chỉnh thông tin không có bút toán.
/// </summary>
public sealed class EInvoiceIssueService(HakDbContext db, InvoiceService invoices, IEInvoiceProvider provider, AuditService audit, IClock clock, EInvoiceOptions options)
{
    private static string Norm(string? v) => (v ?? "").Replace(" ", "").Trim();
    private static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(900, 15 * Math.Pow(2, Math.Max(0, attempts - 1))));

    // ------------------------------------------------------------------ phát hành

    public async Task<EInvoiceDocumentView> IssueAsync(SessionContext ctx, Guid invoiceId, long? expectedRowVersion = null)
    {
        ctx.Require(Permissions.EInvoiceIssue); ctx.Require(Permissions.VoucherPost);
        RequireProvider();
        var docId = await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (expectedRowVersion is { } rv && inv.RowVersion != rv)
                throw new DomainException(ErrorCodes.ConcurrencyConflict, $"Hóa đơn {inv.Number} đã được thay đổi (phiên bản {inv.RowVersion}, bạn đang xem phiên bản {rv}). Vui lòng tải lại.");
            if (inv.Kind != InvoiceKind.Sales) throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ hóa đơn bán do công ty phát hành; hóa đơn mua nhận từ nhà cung cấp.");
            if (inv.Status != InvoiceStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number} ở trạng thái {inv.Status}; chỉ phát hành hóa đơn nháp.");
            // Phát hành rồi ghi sổ là một thao tác của người phát hành: người ghi sổ không được trùng người lập (kiểm trước để không phát hành hợp pháp rồi mới bị chặn ghi sổ).
            if ((await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId)).RequireDistinctApprover && inv.CreatedBy == ctx.UserId)
                throw new DomainException(ErrorCodes.SodViolation, "Người phát hành (và ghi sổ) hóa đơn không được trùng người lập.");
            if (await db.EInvoiceDocuments.AnyAsync(d => d.InvoiceId == inv.Id && d.Status != EInvoiceDocStatus.Rejected))
                throw new DomainException(ErrorCodes.DuplicateSource, $"Hóa đơn {inv.Number} đã có lệnh phát hành điện tử.");
            var rejected = await db.EInvoiceDocuments.FirstOrDefaultAsync(d => d.InvoiceId == inv.Id && d.Status == EInvoiceDocStatus.Rejected);
            if (rejected is not null) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hóa đơn {inv.Number} có lần phát hành bị từ chối; dùng Phát hành lại.");

            EInvoiceRelation relation = EInvoiceRelation.Original; EInvoiceAdjustNature nature = EInvoiceAdjustNature.None; Guid? relatedId = null; string? reason = null;
            if (inv.ReplacesInvoiceId is { } replaced)
            {
                relation = EInvoiceRelation.Replacement; relatedId = await IssuedDocumentOfAsync(ctx.CompanyId, replaced, "bị thay thế");
                reason = inv.Description;
            }
            else if (inv.Purpose != InvoicePurpose.Normal)
            {
                relation = EInvoiceRelation.Adjustment;
                nature = inv.Purpose == InvoicePurpose.Increase ? EInvoiceAdjustNature.Increase : EInvoiceAdjustNature.Decrease;
                relatedId = await IssuedDocumentOfAsync(ctx.CompanyId, inv.AdjustsInvoiceId!.Value, "được điều chỉnh");
                reason = inv.Description;
            }
            await invoices.PreflightIssueAsync(ctx, inv.Id);
            var doc = NewDocument(ctx, inv.Id, relation, nature, relatedId, reason, inv.InvoiceDate);
            db.EInvoiceDocuments.Add(doc);
            Enqueue(ctx, doc);
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.issue.queue", "einvoice_document", doc.Id.ToString(), new { inv.Number, relation, nature, oldSeries = inv.InvoiceSeries, oldNo = inv.InvoiceNo });
            await db.SaveChangesAsync();
            return doc.Id;
        });
        return await DispatchAsync(ctx, docId);
    }

    /// <summary>Phát hành lại: lần bị từ chối (đổi khóa chống trùng để không nhận lại kết quả từ chối cũ), hoặc hóa đơn đã phát hành nhưng ghi sổ lỗi (ghi sổ lại).</summary>
    public async Task<EInvoiceDocumentView> RetryAsync(SessionContext ctx, Guid documentId)
    {
        ctx.Require(Permissions.EInvoiceIssue);
        RequireProvider();
        var again = await db.InTransactionAsync(async () =>
        {
            var doc = await db.EInvoiceDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn điện tử không tồn tại.");
            if (doc.Status == EInvoiceDocStatus.Rejected)
            {
                if (doc.InvoiceId is { } invoiceId) await invoices.PreflightIssueAsync(ctx, invoiceId);
                doc.Attempt++; doc.Status = EInvoiceDocStatus.Issuing; doc.LastError = null; doc.RowVersion++;
                Enqueue(ctx, doc);
                await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.issue.retry", "einvoice_document", doc.Id.ToString(), new { doc.Attempt }, doc.RowVersion);
                await db.SaveChangesAsync();
                return true;
            }
            return false;
        });
        if (again) return await DispatchAsync(ctx, documentId);
        var d = await db.EInvoiceDocuments.AsNoTracking().FirstAsync(x => x.Id == documentId);
        if (d.Status == EInvoiceDocStatus.Issuing) return await DispatchAsync(ctx, documentId);
        if (d.Status == EInvoiceDocStatus.Issued && d.PostError is not null) { await PostIssuedAsync(ctx, documentId); return await ViewAsync(ctx, documentId); }
        throw new DomainException(ErrorCodes.InvalidStateTransition, "Hóa đơn điện tử này không cần phát hành lại.");
    }

    /// <summary>Gửi mọi lệnh đang chờ đến hạn (sau lỗi tạm thời). Chạy tay hoặc theo lịch; an toàn khi chạy lặp vì khóa chống trùng.</summary>
    public async Task<ProcessResult> ProcessPendingAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.EInvoiceIssue);
        RequireProvider();
        var now = clock.UtcNow;
        var due = await db.EInvoiceOutbox.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId && o.State == EInvoiceOutboxState.Pending && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
            .OrderBy(o => o.CreatedAt).Select(o => o.DocumentId).Distinct().ToListAsync();
        int issued = 0, rejected = 0, pending = 0;
        foreach (var id in due)
        {
            var v = await DispatchAsync(ctx, id);
            if (v.Status == nameof(EInvoiceDocStatus.Issued)) issued++; else if (v.Status == nameof(EInvoiceDocStatus.Rejected)) rejected++; else pending++;
        }
        return new ProcessResult(due.Count, issued, rejected, pending);
    }

    /// <summary>Lệnh đang chờ đến hạn của MỌI công ty (dịch vụ nền): công ty, hóa đơn điện tử và người đã yêu cầu — người đó là người chịu trách nhiệm ghi sổ kết quả (quyền hiện tại của họ được kiểm lại).</summary>
    public async Task<IReadOnlyList<(Guid CompanyId, Guid DocumentId, Guid RequestedBy)>> DueForWorkerAsync()
    {
        var now = clock.UtcNow;
        var rows = await (from o in db.EInvoiceOutbox.AsNoTracking()
                          join d in db.EInvoiceDocuments.AsNoTracking() on o.DocumentId equals d.Id
                          where o.State == EInvoiceOutboxState.Pending && d.Status == EInvoiceDocStatus.Issuing && (o.NextAttemptAt == null || o.NextAttemptAt <= now)
                          orderby o.CreatedAt
                          select new { o.CompanyId, o.DocumentId, d.RequestedBy }).Take(50).ToListAsync();
        return rows.Select(r => (r.CompanyId, r.DocumentId, r.RequestedBy)).Distinct().ToList();
    }

    /// <summary>Gửi một lệnh đang chờ (dùng cho dịch vụ nền). Lỗi nghiệp vụ không làm hỏng vòng lặp: ghi vào hóa đơn để người dùng thấy.</summary>
    public async Task DispatchOneAsync(SessionContext ctx, Guid documentId)
    {
        if (!provider.Configured) return;
        await DispatchAsync(ctx, documentId);
    }

    private async Task<EInvoiceDocumentView> DispatchAsync(SessionContext ctx, Guid documentId)
    {
        var (doc, outbox, request) = await BuildRequestAsync(ctx, documentId);
        if (doc.Status != EInvoiceDocStatus.Issuing || outbox is null) return await ViewAsync(ctx, documentId);
        ProviderIssueResult result;
        try { result = await provider.IssueAsync(request); }
        catch (EInvoiceTransientException ex)
        {
            await db.InTransactionAsync(async () =>
            {
                var ob = await db.EInvoiceOutbox.FirstAsync(o => o.Id == outbox.Id);
                ob.Attempts++; ob.LastError = ex.Message; ob.NextAttemptAt = clock.UtcNow + Backoff(ob.Attempts); ob.UpdatedAt = clock.UtcNow;
                db.EInvoiceMessages.Add(Message(ctx, documentId, "IN", "Transient", null, "TRANSIENT", ex.Message));
                await db.SaveChangesAsync();
            });
            return await ViewAsync(ctx, documentId);
        }

        // Giao dịch A: ghi nhận kết quả. Đã Issued là bất biến — không bao giờ mất vì ghi sổ lỗi ở bước sau.
        await db.InTransactionAsync(async () =>
        {
            var d = await db.EInvoiceDocuments.FirstAsync(x => x.Id == documentId);
            var ob = await db.EInvoiceOutbox.FirstAsync(o => o.Id == outbox.Id);
            ob.Attempts++; ob.UpdatedAt = clock.UtcNow; ob.NextAttemptAt = null;
            if (result.Outcome == ProviderOutcome.Rejected)
            {
                d.Status = EInvoiceDocStatus.Rejected; d.LastError = $"{result.ErrorCode}: {result.ErrorMessage}"; d.RowVersion++;
                ob.State = EInvoiceOutboxState.Failed; ob.LastError = d.LastError;
                db.EInvoiceMessages.Add(Message(ctx, documentId, "IN", "Rejected", null, result.ErrorCode, result.ErrorMessage));
                await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.issue.rejected", "einvoice_document", d.Id.ToString(), new { result.ErrorCode, result.ErrorMessage }, d.RowVersion);
                await db.SaveChangesAsync();
                return;
            }
            d.Status = EInvoiceDocStatus.Issued; d.ProviderRef = result.ProviderRef; d.Series = result.Series; d.Number = result.Number; d.SignedAt = result.SignedAt;
            d.TaxAuthorityCode = result.TaxAuthorityCode; d.XmlContent = result.Xml; d.XmlSha256 = result.Xml is null ? null : EInvoiceXml.Sha256(result.Xml); d.LastError = null; d.RowVersion++;
            ob.State = EInvoiceOutboxState.Confirmed; ob.LastError = null;
            db.EInvoiceMessages.Add(Message(ctx, documentId, "IN", "Accepted", d.XmlSha256, "OK", $"{d.Series}/{d.Number}"));
            if (d.InvoiceId is { } invId)
            {
                var inv = await db.CommercialInvoices.FirstAsync(i => i.Id == invId);
                inv.InvoiceSeries = d.Series!; inv.InvoiceNo = d.Number!;     // số thật do nhà cung cấp cấp thay cho số tạm / số gõ tay
                await RecordIssuedAsync(ctx, d, inv);
            }
            if (d.Relation == EInvoiceRelation.Replacement && d.RelatedDocumentId is { } oldId)
            {
                var old = await db.EInvoiceDocuments.FirstAsync(x => x.Id == oldId);
                old.Status = EInvoiceDocStatus.Replaced; old.RowVersion++;
                await MarkRecordStatusAsync(ctx, old, EInvoiceStatus.Replaced, d.XmlSha256 ?? d.Id.ToString("N"));
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.issue.accepted", "einvoice_document", d.Id.ToString(), new { d.Series, d.Number, d.TaxAuthorityCode, d.Relation, d.Nature }, d.RowVersion);
            await db.SaveChangesAsync();
        });
        var after = await db.EInvoiceDocuments.AsNoTracking().FirstAsync(x => x.Id == documentId);
        if (after.Status == EInvoiceDocStatus.Issued && after.InvoiceId is not null) await PostIssuedAsync(ctx, documentId);
        return await ViewAsync(ctx, documentId);
    }

    /// <summary>Giao dịch B: ghi sổ hóa đơn ERP bằng số thật. Lỗi (kỳ khóa, thiếu cấu hình, không đảo được hóa đơn bị thay…) ghi lại ở <c>PostError</c> để xử lý rồi Phát hành lại → ghi sổ lại.</summary>
    public async Task PostIssuedAsync(SessionContext ctx, Guid documentId)
    {
        var doc = await db.EInvoiceDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId && d.CompanyId == ctx.CompanyId);
        if (doc.Status != EInvoiceDocStatus.Issued || doc.InvoiceId is not { } invId) return;
        string? error = null;
        try
        {
            var inv = await db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == invId);
            if (inv.Status == InvoiceStatus.Draft) await invoices.PostAsync(ctx, invId);
        }
        catch (DomainException ex) { error = ex.Message; }
        catch (DbUpdateException) { error = "Hóa đơn đã phát hành nhưng ghi sổ gặp lỗi cơ sở dữ liệu. Đối chiếu trạng thái và thử ghi sổ lại trên cùng hóa đơn; không phát hành hóa đơn mới."; }
        await db.InTransactionAsync(async () =>
        {
            var d = await db.EInvoiceDocuments.FirstAsync(x => x.Id == documentId);
            if (d.PostError == error) return;
            d.PostError = error; d.RowVersion++;
            if (error is not null) await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.post_failed", "einvoice_document", d.Id.ToString(), new { error }, d.RowVersion);
            await db.SaveChangesAsync();
        });
    }

    // ------------------------------------------------------------------ hóa đơn thay thế, điều chỉnh thông tin, hóa đơn đã phát hành ngoài ERP

    /// <summary>
    /// Lập hóa đơn thay thế NHÁP từ hóa đơn bán đã ghi sổ: sao chép các dòng (cùng phiếu kho / đơn hàng), áp thay đổi giá / thuế suất / chiết khấu / đối tượng. Sau đó phát hành như hóa đơn thường;
    /// khi cơ quan thuế chấp nhận, hóa đơn gốc được đảo và hóa đơn thay thế được ghi sổ trong cùng giao dịch.
    /// </summary>
    public async Task<CommercialInvoice> CreateReplacementDraftAsync(SessionContext ctx, Guid originalInvoiceId, string reason, IReadOnlyList<ReplacementLineChange>? changes = null, Guid? newPartnerId = null)
    {
        ctx.Require(Permissions.InvoiceDocument); ctx.Require(Permissions.EInvoiceIssue);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do thay thế.");
        var orig = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == originalInvoiceId && i.CompanyId == ctx.CompanyId)
                   ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn gốc không tồn tại.");
        await IssuedDocumentOfAsync(ctx.CompanyId, orig.Id, "bị thay thế");
        var byLine = (changes ?? []).ToDictionary(c => c.LineNo);
        foreach (var c in byLine.Values) if (orig.Lines.All(l => l.LineNo != c.LineNo)) throw new DomainException(ErrorCodes.NotFound, $"Hóa đơn gốc không có dòng {c.LineNo}.");
        var lines = orig.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            byLine.TryGetValue(l.LineNo, out var ch);
            // Đổi tỷ lệ chiết khấu thì bỏ số tiền chiết khấu cũ; không đổi thì giữ nguyên cách nhập của hóa đơn gốc (% hoặc số tiền).
            var pct = ch?.DiscountPercent ?? l.DiscountPercent;
            decimal? amount = ch?.DiscountPercent is not null || l.DiscountPercent is not null || l.DiscountAmount <= 0 ? null : l.DiscountAmount;
            return l.StockDocumentId is not null
                ? new InvoiceLineInput(ch?.UnitPrice ?? l.UnitPrice, l.StockDocumentId, l.StockLineNo, l.StockQuantity, l.Basis, VatRate: ch?.VatRate ?? l.VatRate, Description: l.Description,
                    DiscountPercent: pct, DiscountAmount: amount)
                : new InvoiceLineInput(ch?.UnitPrice ?? l.UnitPrice, Basis: PricingBasis.Quantity, Quantity: l.BasisQuantity, AccountId: l.AccountId, VatRate: ch?.VatRate ?? l.VatRate, Description: l.Description,
                    OrderId: l.OrderId, OrderLineNo: l.OrderLineNo, DiscountPercent: pct, DiscountAmount: amount);
        }).ToList();
        var input = new InvoiceInput(orig.Kind, newPartnerId ?? orig.PartnerId, InvoiceService.AwaitingSeries, "", orig.InvoiceDate, orig.PostingDate,
            $"Thay thế hóa đơn {orig.InvoiceSeries}/{orig.InvoiceNo}: {reason.Trim()}", lines, orig.DueDate, orig.Currency, orig.ExchangeRate, orig.PaymentDiscountPercent, orig.PaymentDiscountDays,
            AwaitIssue: true, ReplacesInvoiceId: orig.Id, PaymentDiscountOptions: orig.PaymentDiscountOptions);
        return await invoices.CreateAsync(ctx, input);
    }

    /// <summary>Hóa đơn điều chỉnh thông tin (sai tên, địa chỉ… không đổi tiền, thuế): chỉ phát hành văn bản liên kết hóa đơn gốc, KHÔNG có bút toán.</summary>
    public async Task<EInvoiceDocumentView> AdjustInformationAsync(SessionContext ctx, Guid relatedDocumentId, string reason, IReadOnlyList<InformationChange> changes)
    {
        ctx.Require(Permissions.EInvoiceIssue);
        RequireProvider();
        if (string.IsNullOrWhiteSpace(reason) || changes.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Phải nhập lý do và ít nhất một thông tin được điều chỉnh.");
        var docId = await db.InTransactionAsync(async () =>
        {
            var related = await db.EInvoiceDocuments.FirstOrDefaultAsync(d => d.Id == relatedDocumentId && d.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn gốc không tồn tại.");
            if (related.Status != EInvoiceDocStatus.Issued) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ điều chỉnh thông tin của hóa đơn đã phát hành.");
            var text = $"{reason.Trim()} — " + string.Join("; ", changes.Select(c => $"{c.Field}: '{c.OldValue}' → '{c.NewValue}'"));
            var doc = NewDocument(ctx, null, EInvoiceRelation.Adjustment, EInvoiceAdjustNature.Information, related.Id, text, clock.UtcNow.Date is var d ? DateOnly.FromDateTime(d) : default);
            db.EInvoiceDocuments.Add(doc);
            Enqueue(ctx, doc);
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.adjust_information", "einvoice_document", doc.Id.ToString(), new { related = $"{related.Series}/{related.Number}", changes });
            await db.SaveChangesAsync();
            return doc.Id;
        });
        return await DispatchAsync(ctx, docId);
    }

    /// <summary>Ghi nhận hóa đơn bán ĐÃ phát hành ngoài HAK (phần mềm khác, đã có số thật): tạo bản ghi Issued không qua nhà cung cấp để làm gốc cho điều chỉnh / thay thế sau này.</summary>
    public async Task<EInvoiceDocumentView> RegisterExternalAsync(SessionContext ctx, Guid invoiceId)
    {
        ctx.Require(Permissions.EInvoiceIssue);
        var id = await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (inv.Kind != InvoiceKind.Sales || inv.Purpose != InvoicePurpose.Normal || inv.Status != InvoiceStatus.Posted)
                throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ ghi nhận hóa đơn bán thường đã ghi sổ.");
            if (inv.InvoiceSeries == InvoiceService.AwaitingSeries) throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn chưa có số thật.");
            if (await db.EInvoiceDocuments.AnyAsync(d => d.InvoiceId == inv.Id)) throw new DomainException(ErrorCodes.DuplicateSource, "Hóa đơn đã có bản ghi điện tử.");
            var doc = new EInvoiceDocument
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, InvoiceId = inv.Id, Provider = "EXTERNAL", TemplateCode = options.TemplateCode, Series = inv.InvoiceSeries, Number = inv.InvoiceNo,
                IssueDate = inv.InvoiceDate, Status = EInvoiceDocStatus.Issued, Relation = EInvoiceRelation.Original, RequestedBy = ctx.UserId, RequestedAt = clock.UtcNow, RowVersion = 1,
            };
            db.EInvoiceDocuments.Add(doc);
            await audit.AppendAsync(ctx.CompanyId, ctx, "einvoice.register_external", "einvoice_document", doc.Id.ToString(), new { invoice = inv.Number, doc.Series, doc.Number });
            await db.SaveChangesAsync();
            return doc.Id;
        });
        return await ViewAsync(ctx, id);
    }

    // ------------------------------------------------------------------ truy vấn

    public async Task<IReadOnlyList<EInvoiceDocumentView>> ListAsync(SessionContext ctx, EInvoiceDocStatus? status = null, bool attentionOnly = false, int take = 200)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.EInvoiceDocuments.AsNoTracking().Where(d => d.CompanyId == ctx.CompanyId);
        if (status is { } s) q = q.Where(d => d.Status == s);
        if (attentionOnly) q = q.Where(d => d.Status == EInvoiceDocStatus.Issuing || d.Status == EInvoiceDocStatus.Rejected || d.PostError != null);
        var docs = await q.OrderByDescending(d => d.RequestedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();
        return await ToViewsAsync(ctx.CompanyId, docs);
    }

    public async Task<EInvoiceDocumentView> ViewAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var d = await db.EInvoiceDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn điện tử không tồn tại.");
        return (await ToViewsAsync(ctx.CompanyId, [d]))[0];
    }

    public async Task<IReadOnlyList<EInvoiceMessage>> MessagesAsync(SessionContext ctx, Guid documentId)
    {
        ctx.Require(Permissions.ReportView);
        return await db.EInvoiceMessages.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.DocumentId == documentId).OrderBy(m => m.At).ToListAsync();
    }

    private async Task<IReadOnlyList<EInvoiceDocumentView>> ToViewsAsync(Guid companyId, List<EInvoiceDocument> docs)
    {
        var invIds = docs.Where(d => d.InvoiceId != null).Select(d => d.InvoiceId!.Value).ToList();
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).Where(i => invIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var partnerIds = invs.Values.Select(i => i.PartnerId).Distinct().ToList();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => partnerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var ids = docs.Select(d => d.Id).ToList();
        var outbox = (await db.EInvoiceOutbox.AsNoTracking().Where(o => ids.Contains(o.DocumentId)).OrderBy(o => o.CreatedAt).ToListAsync()).GroupBy(o => o.DocumentId).ToDictionary(g => g.Key, g => g.Last());
        var relIds = docs.Where(d => d.RelatedDocumentId != null).Select(d => d.RelatedDocumentId!.Value).ToList();
        var related = await db.EInvoiceDocuments.AsNoTracking().Where(d => relIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => $"{d.Series}/{d.Number}");
        return docs.Select(d =>
        {
            var inv = d.InvoiceId is { } i && invs.TryGetValue(i, out var x) ? x : null;
            var ob = outbox.GetValueOrDefault(d.Id);
            return new EInvoiceDocumentView(d.Id, d.InvoiceId, inv?.Number, d.Status.ToString(), d.Relation.ToString(), d.Nature.ToString(), d.Series, d.Number, d.IssueDate, d.TaxAuthorityCode, d.Reason,
                d.LastError ?? ob?.LastError, d.PostError, ob?.Attempts ?? 0, ob?.State.ToString(), ob?.NextAttemptAt, d.RowVersion, inv is null ? null : partners.GetValueOrDefault(inv.PartnerId),
                inv?.Lines.Sum(l => l.FunctionalAmount + l.VatAmount), d.RelatedDocumentId, d.RelatedDocumentId is { } r ? related.GetValueOrDefault(r) : null, d.Provider);
        }).ToList();
    }

    // ------------------------------------------------------------------ nội bộ

    private void RequireProvider()
    {
        if (!provider.Configured) throw new DomainException(ErrorCodes.ValidationFailed, "Chưa cấu hình nhà cung cấp hóa đơn điện tử (Hak:EInvoice:Provider); chưa phát hành được.");
    }

    private EInvoiceDocument NewDocument(SessionContext ctx, Guid? invoiceId, EInvoiceRelation relation, EInvoiceAdjustNature nature, Guid? relatedId, string? reason, DateOnly issueDate)
        => new()
        {
            Id = Ids.New(), CompanyId = ctx.CompanyId, InvoiceId = invoiceId, Provider = provider.Name, TemplateCode = options.TemplateCode, IssueDate = issueDate, Status = EInvoiceDocStatus.Issuing,
            Relation = relation, RelatedDocumentId = relatedId, Nature = nature, Reason = reason, Attempt = 1, RequestedBy = ctx.UserId, RequestedAt = clock.UtcNow, RowVersion = 1,
        };

    private void Enqueue(SessionContext ctx, EInvoiceDocument doc)
    {
        db.EInvoiceOutbox.Add(new EInvoiceOutboxItem { Id = Ids.New(), CompanyId = ctx.CompanyId, DocumentId = doc.Id, Operation = "Issue", IdempotencyKey = $"{doc.Id:N}-{doc.Attempt}", CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow });
        db.EInvoiceMessages.Add(Message(ctx, doc.Id, "OUT", "Queued", null, null, $"lần {doc.Attempt}"));
    }

    private EInvoiceMessage Message(SessionContext ctx, Guid? documentId, string direction, string kind, string? sha, string? code, string? detail)
        => new() { Id = Ids.New(), CompanyId = ctx.CompanyId, DocumentId = documentId, Direction = direction, Kind = kind, PayloadSha256 = sha, Code = code, Detail = detail, At = clock.UtcNow };

    private async Task<Guid> IssuedDocumentOfAsync(Guid companyId, Guid invoiceId, string role)
    {
        var d = await db.EInvoiceDocuments.AsNoTracking().Where(x => x.CompanyId == companyId && x.InvoiceId == invoiceId && x.Status == EInvoiceDocStatus.Issued).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
        return d ?? throw new DomainException(ErrorCodes.ValidationFailed, $"Hóa đơn gốc {role} chưa có bản phát hành điện tử trong HAK. Phát hành hóa đơn gốc qua HAK, hoặc ghi nhận hóa đơn đã phát hành ngoài hệ thống trước.");
    }

    private async Task<(EInvoiceDocument Doc, EInvoiceOutboxItem? Outbox, ProviderIssueRequest Request)> BuildRequestAsync(SessionContext ctx, Guid documentId)
    {
        var doc = await db.EInvoiceDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn điện tử không tồn tại.");
        var outbox = await db.EInvoiceOutbox.AsNoTracking().Where(o => o.DocumentId == documentId && o.State == EInvoiceOutboxState.Pending).OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
        var seller = new ProviderParty(company.Name, Norm(company.TaxCode), null);
        ProviderParty buyer; string currency = company.FunctionalCurrency; decimal? rate = null;
        var lines = new List<ProviderLine>(); decimal before = 0, vat = 0;
        if (doc.InvoiceId is { } invId)
        {
            var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstAsync(i => i.Id == invId);
            var p = await db.BusinessPartners.AsNoTracking().FirstAsync(x => x.Id == inv.PartnerId);
            buyer = new ProviderParty(p.Name, Norm(p.TaxCode), p.Address);
            currency = inv.Currency ?? company.FunctionalCurrency; rate = inv.ExchangeRate;
            var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
            foreach (var l in inv.Lines.OrderBy(x => x.LineNo))
            {
                var item = l.ItemId is { } iid && items.TryGetValue(iid, out var it) ? it : null;
                lines.Add(new ProviderLine(l.LineNo, l.Description ?? item?.Name ?? "Hàng hóa, dịch vụ", item?.Unit, l.BasisQuantity, l.UnitPrice, l.DiscountAmount, l.Amount, l.VatRate, l.VatAmount));
            }
            before = inv.Lines.Sum(l => l.FunctionalAmount); vat = inv.Lines.Sum(l => l.VatAmount);
        }
        else buyer = new ProviderParty("", null, null);
        EInvoiceDocument? related = doc.RelatedDocumentId is { } rid ? await db.EInvoiceDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == rid) : null;
        if (doc.InvoiceId is null && related?.InvoiceId is { } relInv)       // điều chỉnh thông tin: người mua lấy theo hóa đơn gốc
        {
            var o = await db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == relInv);
            var p = await db.BusinessPartners.AsNoTracking().FirstAsync(x => x.Id == o.PartnerId);
            buyer = new ProviderParty(p.Name, Norm(p.TaxCode), p.Address);
        }
        var key = outbox?.IdempotencyKey ?? $"{doc.Id:N}-{doc.Attempt}";
        var draft = new ProviderIssueRequest(key, doc.TemplateCode, doc.IssueDate, doc.Relation, doc.Nature, doc.Reason, related?.TemplateCode, related?.Series, related?.Number, related?.IssueDate,
            seller, buyer, currency, rate, lines, before, vat, before + vat, "");
        var xml = EInvoiceXml.Build(draft);
        var request = draft with { XmlDraft = xml };
        if (outbox is not null && doc.Status == EInvoiceDocStatus.Issuing)
        {
            // Nhật ký gửi (băm nội dung); ghi ở giao dịch riêng để vẫn còn nếu lần gọi mạng sau đó lỗi.
            await db.InTransactionAsync(async () =>
            {
                db.EInvoiceMessages.Add(Message(ctx, documentId, "OUT", "Issue", EInvoiceXml.Sha256(xml), null, $"khóa {key}"));
                await db.SaveChangesAsync();
            });
        }
        return (doc, outbox, request);
    }

    /// <summary>Nguồn 1 của đối soát ba nguồn được điền TỰ ĐỘNG khi hóa đơn gốc / thay thế / điều chỉnh được chấp nhận (không cần nhập tệp). Hóa đơn điều chỉnh ghi số dương, trạng thái Adjustment, kèm số hóa đơn gốc.</summary>
    private async Task RecordIssuedAsync(SessionContext ctx, EInvoiceDocument d, CommercialInvoice inv)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
        var own = Norm(company.TaxCode);
        if (own.Length == 0) return;
        var lines = await db.CommercialInvoiceLines.AsNoTracking().Where(l => l.InvoiceId == inv.Id).ToListAsync();
        var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == inv.PartnerId);
        var related = d.RelatedDocumentId is { } rid ? await db.EInvoiceDocuments.AsNoTracking().FirstAsync(x => x.Id == rid) : null;
        var batch = new EInvoiceImportBatch { Id = Ids.New(), CompanyId = ctx.CompanyId, SourceName = $"Phát hành từ HAK {d.Series}/{d.Number}", SourceSha256 = d.XmlSha256 ?? Ids.New().ToString("N"), Imported = 1, ImportedAt = clock.UtcNow, ImportedBy = ctx.UserId };
        db.EInvoiceImportBatches.Add(batch);
        var rec = new EInvoiceRecord
        {
            Id = Ids.New(), CompanyId = ctx.CompanyId, Direction = TaxDirection.Output, SellerTaxCode = own, SellerName = company.Name, BuyerTaxCode = Norm(partner.TaxCode) is { Length: > 0 } b ? b : null, BuyerName = partner.Name,
            Series = d.Series!, Number = d.Number!, IssueDate = d.IssueDate, Currency = inv.Currency, ExchangeRate = inv.ExchangeRate,
            AmountConvention = d.Relation == EInvoiceRelation.Adjustment ? EInvoiceAmountConvention.MagnitudeWithNature : EInvoiceAmountConvention.Signed, AdjustmentNature = d.Nature,
            AmountBeforeVat = lines.Sum(l => l.FunctionalAmount), VatAmount = lines.Sum(l => l.VatAmount), TotalPayment = lines.Sum(l => l.FunctionalAmount + l.VatAmount),
            Status = d.Relation == EInvoiceRelation.Replacement ? EInvoiceStatus.Replacement : d.Relation == EInvoiceRelation.Adjustment ? EInvoiceStatus.Adjustment : EInvoiceStatus.Original, RelatedSeries = related?.Series, RelatedNumber = related?.Number,
            LookupCode = d.TaxAuthorityCode, ImportBatchId = batch.Id, ImportedAt = clock.UtcNow, ImportedBy = ctx.UserId,
        };
        rec.RateLines = lines.GroupBy(l => l.VatRate).Select(g => new EInvoiceRateLine { Id = Ids.New(), EInvoiceId = rec.Id, VatRate = g.Key, AmountBeforeVat = g.Sum(l => l.FunctionalAmount), VatAmount = g.Sum(l => l.VatAmount) }).ToList();
        db.EInvoiceRecords.Add(rec);
    }

    private async Task MarkRecordStatusAsync(SessionContext ctx, EInvoiceDocument d, EInvoiceStatus status, string sourceKey)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
        var own = Norm(company.TaxCode);
        var rec = await db.EInvoiceRecords.AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == ctx.CompanyId && r.Direction == TaxDirection.Output && r.SellerTaxCode == own && r.Series == d.Series && r.Number == d.Number);
        if (rec is null) return;     // hóa đơn gốc phát hành ngoài HAK và chưa nhập tệp: đối soát sẽ báo khi nhập
        var batch = new EInvoiceImportBatch { Id = Ids.New(), CompanyId = ctx.CompanyId, SourceName = $"Trạng thái từ HAK {d.Series}/{d.Number}", SourceSha256 = "STATUS-" + sourceKey + "-" + status, ImportedAt = clock.UtcNow, ImportedBy = ctx.UserId };
        db.EInvoiceImportBatches.Add(batch);
        db.EInvoiceStatusChanges.Add(new EInvoiceStatusChange { Id = Ids.New(), EInvoiceId = rec.Id, NewStatus = status, ImportBatchId = batch.Id, At = clock.UtcNow });
    }
}
