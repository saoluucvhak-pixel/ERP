using Hak.Application.Fx;
using Hak.Domain.Fx;

namespace Hak.Api;

public sealed record FxRevalueRequest(DateOnly Date, List<FxRate> Rates, string RateSource);

public static class FxEndpoints
{
    public static void MapFxEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/fx");
        api.MapGet("/balances", async (HttpContext http, DateOnly asOf, FxService s) => Results.Ok(await s.BalancesAsync(await http.SessionAsync(), asOf)));
        api.MapGet("/revaluations", async (HttpContext http, FxService s) => Results.Ok(await s.RunsAsync(await http.SessionAsync())));
        api.MapPost("/revaluations", async (HttpContext http, FxRevalueRequest r, FxService s) =>
            Results.Ok(await s.RevalueAsync(await http.SessionAsync(), r.Date, r.Rates, r.RateSource)));
        // Thu/chi ngoại tệ theo hóa đơn: preview the voucher (book values, realised difference), then create it as a Draft.
        api.MapPost("/payments/preview", async (HttpContext http, FxPaymentInput input, FxPaymentService s) =>
            Results.Ok(await s.PreviewAsync(await http.SessionAsync(), input)));
        api.MapPost("/payments", async (HttpContext http, FxPaymentInput input, FxPaymentService s) =>
        {
            var v = await s.CreateDraftAsync(await http.SessionAsync(), input);
            return Results.Ok(new { v.Id, v.Number, status = v.Status.ToString() });
        });
        api.MapPost("/revaluations/{id:guid}/reverse", async (HttpContext http, Guid id, ReasonRequest r, FxService s) =>
        { await s.ReverseAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
