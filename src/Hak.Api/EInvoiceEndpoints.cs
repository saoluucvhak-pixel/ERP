using Hak.Application.EInvoice;
using Hak.Application.Trade;
using Hak.Domain.Tax;

namespace Hak.Api;

public sealed record ReplacementRequest(string Reason, IReadOnlyList<ReplacementLineChange>? Changes, Guid? NewPartnerId, DateOnly? InvoiceDate = null, DateOnly? PostingDate = null);
public sealed record RegisterExternalRequest(string Reason);
public sealed record InformationRequest(string Reason, IReadOnlyList<InformationChange> Changes);
public sealed record RiskNoticeRequest(string AuthorityNoticeNo, DateOnly NoticeDate, DateOnly ReceivedDate, DateOnly? ResponseDeadline, string Reason, IReadOnlyList<RiskNoticeLineInput> Lines);
public sealed record RiskNoticeRespondRequest(long RowVersion, string? Note, Guid? ResponseNoticeId);
public sealed record RiskNoticeCloseRequest(long RowVersion);
public sealed record NoticeRequest(ErrorNoticeKind Kind, string Reason, IReadOnlyList<NoticeLineInput> Lines);
public sealed record NoticeSendRequest(long RowVersion);
public sealed record InboundAdjustmentRequest(Hak.Domain.Trade.InvoicePurpose Purpose, Hak.Domain.Trade.DiscountKind? DiscountKind, DateOnly PostingDate);

/// <summary>Hóa đơn điện tử phát hành từ ERP, thay thế / điều chỉnh, thông báo sai sót (docs/12).</summary>
public static class EInvoiceEndpoints
{
    public static void MapEInvoiceEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/einvoice");
        api.MapGet("/status", (IEInvoiceProvider p) => Results.Ok(new { provider = p.Name, configured = p.Configured }));
        api.MapGet("/documents", async (HttpContext http, EInvoiceDocStatus? status, bool? attention, EInvoiceIssueService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), status, attention == true)));
        api.MapGet("/documents/{id:guid}/messages", async (HttpContext http, Guid id, EInvoiceIssueService s) => Results.Ok(await s.MessagesAsync(await http.SessionAsync(), id)));
        api.MapPost("/invoices/{invoiceId:guid}/issue", async (HttpContext http, Guid invoiceId, long rowVersion, EInvoiceIssueService s) => Results.Ok(await s.IssueAsync(await http.SessionAsync(), invoiceId, rowVersion)));
        api.MapPost("/invoices/{invoiceId:guid}/register-external", async (HttpContext http, Guid invoiceId, RegisterExternalRequest r, EInvoiceIssueService s) => Results.Ok(await s.RegisterExternalAsync(await http.SessionAsync(), invoiceId, r.Reason)));
        api.MapPost("/invoices/{invoiceId:guid}/replacement", async (HttpContext http, Guid invoiceId, ReplacementRequest r, EInvoiceIssueService s) =>
        {
            var inv = await s.CreateReplacementDraftAsync(await http.SessionAsync(), invoiceId, r.Reason, r.Changes, r.NewPartnerId, r.InvoiceDate, r.PostingDate);
            return Results.Ok(new { inv.Id, inv.Number, inv.RowVersion });
        });
        api.MapPost("/documents/{id:guid}/retry", async (HttpContext http, Guid id, EInvoiceIssueService s) => Results.Ok(await s.RetryAsync(await http.SessionAsync(), id)));
        api.MapPost("/process-pending", async (HttpContext http, EInvoiceIssueService s) => Results.Ok(await s.ProcessPendingAsync(await http.SessionAsync())));
        api.MapPost("/documents/{id:guid}/adjust-information", async (HttpContext http, Guid id, InformationRequest r, EInvoiceIssueService s) => Results.Ok(await s.AdjustInformationAsync(await http.SessionAsync(), id, r.Reason, r.Changes)));

        api.MapGet("/inbound-suggestions", async (HttpContext http, EInvoiceInboundService s) => Results.Ok(await s.SuggestionsAsync(await http.SessionAsync())));
        api.MapPost("/inbound/{eInvoiceId:guid}/adjustment-draft", async (HttpContext http, Guid eInvoiceId, InboundAdjustmentRequest r, EInvoiceInboundService s) =>
        {
            var inv = await s.CreateAdjustmentDraftAsync(await http.SessionAsync(), eInvoiceId, r.Purpose, r.DiscountKind, r.PostingDate);
            return Results.Ok(new { inv.Id, inv.Number, inv.RowVersion });
        });
        // Thông báo 01/TB-RSĐT của cơ quan thuế (nhận vào): ghi nhận, theo dõi hạn, phản hồi, đóng.
        api.MapGet("/risk-notices", async (HttpContext http, EInvoiceRiskNoticeService s) => Results.Ok(await s.ListAsync(await http.SessionAsync())));
        api.MapPost("/risk-notices", async (HttpContext http, RiskNoticeRequest r, EInvoiceRiskNoticeService s) => Results.Ok(await s.CreateAsync(await http.SessionAsync(), r.AuthorityNoticeNo, r.NoticeDate, r.ReceivedDate, r.ResponseDeadline, r.Reason, r.Lines)));
        api.MapPost("/risk-notices/{id:guid}/respond", async (HttpContext http, Guid id, RiskNoticeRespondRequest r, EInvoiceRiskNoticeService s) => Results.Ok(await s.RespondAsync(await http.SessionAsync(), id, r.RowVersion, r.Note, r.ResponseNoticeId)));
        api.MapPost("/risk-notices/{id:guid}/close", async (HttpContext http, Guid id, RiskNoticeCloseRequest r, EInvoiceRiskNoticeService s) => Results.Ok(await s.CloseAsync(await http.SessionAsync(), id, r.RowVersion)));
        api.MapGet("/notices", async (HttpContext http, EInvoiceNoticeService s) => Results.Ok(await s.ListAsync(await http.SessionAsync())));
        api.MapPost("/notices", async (HttpContext http, NoticeRequest r, EInvoiceNoticeService s) => Results.Ok(await s.CreateAsync(await http.SessionAsync(), r.Kind, r.Reason, r.Lines)));
        api.MapPost("/notices/{id:guid}/send", async (HttpContext http, Guid id, NoticeSendRequest r, EInvoiceNoticeService s) => Results.Ok(await s.SendAsync(await http.SessionAsync(), id, r.RowVersion)));
    }
}
