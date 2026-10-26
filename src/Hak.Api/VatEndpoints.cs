using Hak.Application.Setup;
using Hak.Application.Tax;
using Hak.Data;
using Hak.Domain.Security;
using Hak.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

public sealed record EInvoiceImportRequest(string SourceName, List<EInvoiceInput> Invoices);
public sealed record DeclarationRequest(DateOnly PeriodFrom, DateOnly PeriodTo, List<DeclarationLineInput> Lines);
public sealed record VatOffsetRequest(Guid PeriodId, Guid[]? InputAccountIds = null, Guid[]? OutputAccountIds = null, bool Quarterly = false);
public sealed record FiledRequest(string FilingReference, DateOnly FiledDate);
public sealed record CompanyProfileRequest(string Name, string? TaxCode);
public sealed record PartnerUpdateRequest(string Name, string? TaxCode, bool IsActive = true);

public static class VatEndpoints
{
    public static void MapVatEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapPut("/admin/companies/{id:guid}", async (HttpContext http, Guid id, CompanyProfileRequest r, SetupService s) =>
        {
            var (user, _) = await http.AuthAsync();
            await s.UpdateCompanyProfileAsync(user.Id, id, r.Name, r.TaxCode);
            return Results.NoContent();
        });

        api.MapPut("/partners/{id:guid}", async (HttpContext http, Guid id, PartnerUpdateRequest r, SetupService s) =>
        { await s.UpdatePartnerAsync(await http.SessionAsync(), id, r.Name, r.TaxCode, r.IsActive); return Results.NoContent(); });

        // The raw request body is the source evidence: its SHA-256 identifies the file and blocks re-import.
        api.MapPost("/vat/einvoices/import", async (HttpContext http, VatService s) =>
        {
            var ctx = await http.SessionAsync();
            using var reader = new StreamReader(http.Request.Body);
            var raw = await reader.ReadToEndAsync();
            var req = System.Text.Json.JsonSerializer.Deserialize<EInvoiceImportRequest>(raw, http.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions)
                      ?? throw new Hak.Domain.Common.DomainException(Hak.Domain.Common.ErrorCodes.PackageSchemaInvalid, "Nội dung rỗng.");
            return Results.Ok(await s.ImportEInvoicesAsync(ctx, req.SourceName, raw, req.Invoices));
        });
        api.MapGet("/vat/einvoices", async (HttpContext http, HakDbContext db, DateOnly from, DateOnly to) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.ReportView);
            return Results.Ok(await db.EInvoiceRecords.AsNoTracking().Where(e => e.CompanyId == ctx.CompanyId && e.IssueDate >= from && e.IssueDate <= to)
                .OrderBy(e => e.IssueDate).Select(e => new { e.Id, direction = e.Direction.ToString(), e.SellerTaxCode, e.SellerName, e.Series, e.Number, e.IssueDate,
                    e.AmountBeforeVat, e.VatAmount, e.TotalPayment, status = e.Status.ToString() }).ToListAsync());
        });
        api.MapGet("/vat/listing", async (HttpContext http, TaxDirection direction, DateOnly from, DateOnly to, VatService s) =>
            Results.Ok((await s.LedgerListingAsync(await http.SessionAsync(), direction, from, to)).Select(x => new
            {
                x.InvoiceId, x.Number, direction = x.Direction.ToString(), x.CounterpartyTaxCode, x.CounterpartyName, x.Series, x.InvoiceNo, x.InvoiceDate, x.PostingDate,
                purpose = x.Purpose.ToString(), x.ReceivedDate, x.AmountBeforeVat, x.VatAmount, byRate = x.ByRate.Select(kv => new { rate = kv.Key, amount = kv.Value.Amount, vat = kv.Value.Vat }),
            })));
        api.MapGet("/vat/reconciliation", async (HttpContext http, DateOnly from, DateOnly to, VatService s) =>
        {
            var r = await s.ReconcileAsync(await http.SessionAsync(), from, to);
            return Results.Ok(new { r.From, r.To, r.Matched, r.IsClean, differences = r.Differences.Select(d => new
            {
                type = d.Type.ToString(), direction = d.Direction?.ToString(), d.Key, d.EInvoiceAmount, d.BooksAmount, d.EInvoiceVat, d.BooksVat, d.Difference, d.Suggestion, d.InvoiceId, d.EInvoiceId,
            }) });
        });
        api.MapGet("/vat/declarations", async (HttpContext http, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.ReportView);
            return Results.Ok(await db.TaxDeclarations.AsNoTracking().Include(d => d.Lines).Where(d => d.CompanyId == ctx.CompanyId).OrderByDescending(d => d.PeriodFrom)
                .Select(d => new { d.Id, d.PeriodFrom, d.PeriodTo, status = d.Status.ToString(), d.FilingReference, d.FiledDate,
                    lines = d.Lines.OrderBy(l => l.IndicatorCode).Select(l => new { l.IndicatorCode, l.Label, l.DeclaredAmount, measure = l.Measure.ToString(), l.VatRate }) }).ToListAsync());
        });
        api.MapPost("/vat/declarations", async (HttpContext http, DeclarationRequest r, VatService s) =>
            Results.Ok(new { (await s.SaveDeclarationAsync(await http.SessionAsync(), r.PeriodFrom, r.PeriodTo, r.Lines)).Id }));
        api.MapPost("/vat/declarations/{id:guid}/filed", async (HttpContext http, Guid id, FiledRequest r, VatService s) =>
        { await s.MarkFiledAsync(await http.SessionAsync(), id, r.FilingReference, r.FiledDate); return Results.NoContent(); });
        api.MapGet("/vat/offset", async (HttpContext http, Guid periodId, string? inputAccounts, string? outputAccounts, bool? quarterly, VatOffsetService s) =>
            Results.Ok(await s.PreviewAsync(await http.SessionAsync(), periodId, ParseIds(inputAccounts), ParseIds(outputAccounts), quarterly ?? false)));
        api.MapPost("/vat/offset", async (HttpContext http, VatOffsetRequest r, VatOffsetService s) => Results.Ok(await s.GenerateAsync(await http.SessionAsync(), r.PeriodId, r.InputAccountIds, r.OutputAccountIds, r.Quarterly)));
        api.MapGet("/vat/payable-summary", async (HttpContext http, DateOnly from, DateOnly to, VatService s) => Results.Ok(await s.PayableSummaryAsync(await http.SessionAsync(), from, to)));
    }

    private static Guid[]? ParseIds(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).ToArray();
}
