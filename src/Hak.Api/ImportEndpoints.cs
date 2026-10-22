
namespace Hak.Api;

public sealed record ImportRequest<T>(IReadOnlyList<T> Rows, bool Commit);
public sealed record AssetImportRequest(string OpeningVoucherNumber, IReadOnlyList<Hak.Application.Setup.AssetImportRow> Rows, bool Commit);
public sealed record OpeningStockRequest(DateOnly Date, string CounterAccountCode, string? Description, IReadOnlyList<Hak.Application.Setup.OpeningStockRow> Rows, bool Commit);
public sealed record OpeningImportRequest(DateOnly Date, string? Description, IReadOnlyList<Hak.Application.Setup.OpeningImportRow> Rows, bool Commit);

/// <summary>Bulk import from Excel / CSV (chart of accounts, partners, items, opening balances, vouchers); commit=false is a dry run.</summary>
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this WebApplication app)
    {
        app.MapPost("/api/import/accounts", async (HttpContext http, ImportRequest<Hak.Application.Setup.AccountImportRow> r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportAccountsAsync(await http.SessionAsync(), r.Rows, r.Commit)));
        app.MapPost("/api/import/partners", async (HttpContext http, ImportRequest<Hak.Application.Setup.PartnerImportRow> r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportPartnersAsync(await http.SessionAsync(), r.Rows, r.Commit)));
        app.MapPost("/api/import/items", async (HttpContext http, ImportRequest<Hak.Application.Setup.ItemImportRow> r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportItemsAsync(await http.SessionAsync(), r.Rows, r.Commit)));
        app.MapPost("/api/import/opening-stock", async (HttpContext http, OpeningStockRequest r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportOpeningStockAsync(await http.SessionAsync(), r.Date, r.CounterAccountCode, r.Description, r.Rows, r.Commit)));
        app.MapPost("/api/import/assets", async (HttpContext http, AssetImportRequest r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportAssetsAsync(await http.SessionAsync(), r.OpeningVoucherNumber, r.Rows, r.Commit)));
        app.MapPost("/api/import/opening-balances", async (HttpContext http, OpeningImportRequest r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportOpeningBalancesAsync(await http.SessionAsync(), r.Date, r.Description, r.Rows, r.Commit)));
        app.MapPost("/api/import/vouchers", async (HttpContext http, ImportRequest<Hak.Application.Setup.VoucherImportRow> r, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ImportVouchersAsync(await http.SessionAsync(), r.Rows, r.Commit)));
        app.MapPost("/api/import/vouchers/submit", async (HttpContext http, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.SubmitImportedAsync(await http.SessionAsync())));
        app.MapPost("/api/import/vouchers/approve-post", async (HttpContext http, Hak.Application.Setup.CatalogImportService s) =>
            Results.Ok(await s.ApproveAndPostImportedAsync(await http.SessionAsync())));
    }
}
