using Hak.Application.Banking;
using Hak.Data;
using Hak.Domain.Banking;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

public sealed record ExecuteRequest(DateOnly ExecutedDate, string BankReference);
public sealed record StatementRequest(Guid BankAccountId, DateOnly FromDate, DateOnly ToDate, decimal OpeningBalance, decimal ClosingBalance,
    string SourceName, string SourceContent, List<StatementLineInput> Lines);
public sealed record ConfirmMatchRequest(Guid StatementLineId, Guid JournalLineId);

public sealed record StatementVoucherRequest(Guid CounterAccountId, Guid? PartnerId, string? Description);

public static class BankingEndpoints
{
    private static object Dto(PaymentOrder p) => new
    {
        p.Id, p.Number, p.BankAccountId, p.PartnerId, p.BusinessEvent, p.Amount, p.RequestedDate, p.Description, p.BeneficiaryAccountNo,
        p.BeneficiaryBank, status = p.Status.ToString(), p.ExecutedDate, p.BankReference, p.VoucherId, p.CancelReason, p.RowVersion,
    };

    public static void MapBankingEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/payment-orders", async (HttpContext http, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var list = await db.PaymentOrders.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).OrderByDescending(p => p.Number).Take(500).ToListAsync();
            return Results.Ok(list.Select(Dto));
        });
        api.MapGet("/payment-orders/page", async (HttpContext http, HakDbContext db, int? skip, int? take) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var q = db.PaymentOrders.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId);
            var s = Math.Max(0, skip ?? 0); var t = Math.Clamp(take ?? 200, 1, 500);
            var total = await q.CountAsync();
            var rows = await q.OrderByDescending(p => p.Number).Skip(s).Take(t).ToListAsync();
            return Results.Ok(new { items = rows.Select(Dto), total, hasMore = s + rows.Count < total });
        });
        api.MapPost("/payment-orders", async (HttpContext http, PaymentOrderInput r, PaymentOrderService s) => Results.Ok(Dto(await s.CreateAsync(await http.SessionAsync(), r))));
        api.MapPost("/payment-orders/{id:guid}/approve", async (HttpContext http, Guid id, long rowVersion, PaymentOrderService s) => Results.Ok(Dto(await s.ApproveAsync(await http.SessionAsync(), id, rowVersion))));
        api.MapPost("/payment-orders/{id:guid}/send", async (HttpContext http, Guid id, PaymentOrderService s) => Results.Ok(Dto(await s.MarkSentAsync(await http.SessionAsync(), id))));
        api.MapPost("/payment-orders/{id:guid}/cancel", async (HttpContext http, Guid id, long rowVersion, ReasonRequest r, PaymentOrderService s) => Results.Ok(Dto(await s.CancelAsync(await http.SessionAsync(), id, r.Reason, rowVersion))));
        api.MapPost("/payment-orders/{id:guid}/execute", async (HttpContext http, Guid id, ExecuteRequest r, PaymentOrderService s) =>
        {
            var (order, voucher) = await s.MarkExecutedAsync(await http.SessionAsync(), id, r.ExecutedDate, r.BankReference);
            return Results.Ok(new { order = Dto(order), voucher = VoucherDto.From(voucher) });
        });
        api.MapPost("/payment-orders/{id:guid}/recreate-voucher", async (HttpContext http, Guid id, PaymentOrderService s) =>
            Results.Ok(VoucherDto.From(await s.RecreateVoucherAsync(await http.SessionAsync(), id))));

        api.MapGet("/bank-statements", async (HttpContext http, Guid bankAccountId, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            return Results.Ok(await db.BankStatements.AsNoTracking().Where(s => s.CompanyId == ctx.CompanyId && s.BankAccountId == bankAccountId)
                .OrderByDescending(s => s.ToDate).Select(s => new { s.Id, s.FromDate, s.ToDate, s.OpeningBalance, s.ClosingBalance, s.SourceName, s.ImportedAt, lines = s.Lines.Count })
                .ToListAsync());
        });
        api.MapPost("/bank-statements", async (HttpContext http, StatementRequest r, BankReconciliationService s) =>
        {
            var st = await s.ImportAsync(await http.SessionAsync(),
                new StatementInput(r.BankAccountId, r.FromDate, r.ToDate, r.OpeningBalance, r.ClosingBalance, r.SourceName, r.SourceContent, r.Lines));
            return Results.Ok(new { st.Id, lines = st.Lines.Count });
        });
        api.MapPost("/bank-statements/{id:guid}/auto-match", async (HttpContext http, Guid id, int? toleranceDays, BankReconciliationService s) =>
            Results.Ok(await s.AutoMatchAsync(await http.SessionAsync(), id, toleranceDays ?? 3)));
        api.MapGet("/bank-statement-lines/{id:guid}/suggestion", async (HttpContext http, Guid id, StatementVoucherService s) =>
            Results.Ok(await s.SuggestAsync(await http.SessionAsync(), id)));
        api.MapPost("/bank-statement-lines/{id:guid}/voucher", async (HttpContext http, Guid id, StatementVoucherRequest r, StatementVoucherService s) =>
        {
            var v = await s.CreateDraftAsync(await http.SessionAsync(), id, r.CounterAccountId, r.PartnerId, r.Description);
            return Results.Ok(new { v.Id, v.Number });
        });
        api.MapPost("/bank-reconciliation/confirm", async (HttpContext http, ConfirmMatchRequest r, BankReconciliationService s) =>
        {
            var m = await s.ConfirmAsync(await http.SessionAsync(), r.StatementLineId, r.JournalLineId);
            return Results.Ok(new { m.Id });
        });
        api.MapPost("/bank-reconciliation/{id:guid}/unmatch", async (HttpContext http, Guid id, ReasonRequest r, BankReconciliationService s) =>
        { await s.UnmatchAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapGet("/bank-reconciliation/matches", async (HttpContext http, Guid bankAccountId, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            return Results.Ok(await (from m in db.ReconciliationMatches
                                     join sl in db.BankStatementLines on m.StatementLineId equals sl.Id
                                     join jl in db.JournalLines on m.JournalLineId equals jl.Id
                                     join e in db.JournalEntries on jl.JournalEntryId equals e.Id
                                     where m.CompanyId == ctx.CompanyId && sl.BankAccountId == bankAccountId && m.VoidedAt == null
                                     orderby sl.TransactionDate
                                     select new { m.Id, origin = m.Origin.ToString(), m.Score, sl.TransactionDate, sl.Amount, sl.BankReference, e.VoucherNumber, jl.PostingDate })
                .AsNoTracking().ToListAsync());
        });
        api.MapGet("/reports/bank-reconciliation", async (HttpContext http, Guid bankAccountId, DateOnly asOf, BankReconciliationService s) =>
            Results.Ok(await s.ReportAsync(await http.SessionAsync(), bankAccountId, asOf)));
    }
}
