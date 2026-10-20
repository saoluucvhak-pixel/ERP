using Hak.Application.Banking;
using Hak.Application.Equity;

namespace Hak.Api;

public sealed record ShareholderUpdateRequest(decimal CommittedCapital, bool IsIndividual, bool IsActive, string? Note);
public sealed record BankAccountFlagsRequest(bool? IsDefault, bool? IsActive);
public sealed record BankActiveRequest(bool Active);
public sealed record DividendDeclareRequest(DateOnly Date);
public sealed record DividendPayRequest(Guid ShareholderId, Guid CashAccountId, DateOnly Date);

public static class EquityEndpoints
{
    public static void MapEquityEndpoints(this WebApplication app)
    {
        var ba = app.MapGroup("/api/bank-accounts");
        ba.MapGet("/partners", async (HttpContext http, Guid? partnerId, BankAccountService s) => Results.Ok(await s.PartnerAccountsAsync(await http.SessionAsync(), partnerId)));
        ba.MapPost("/partners/{partnerId:guid}", async (HttpContext http, Guid partnerId, BankAccountInput i, BankAccountService s) => Results.Ok(new { (await s.AddPartnerAccountAsync(await http.SessionAsync(), partnerId, i)).Id }));
        ba.MapPut("/partners/{id:guid}", async (HttpContext http, Guid id, BankAccountFlagsRequest r, BankAccountService s) => { await s.SetPartnerAccountAsync(await http.SessionAsync(), id, r.IsDefault, r.IsActive); return Results.NoContent(); });
        ba.MapGet("/company", async (HttpContext http, BankAccountService s) => Results.Ok(await s.CompanyAccountsAsync(await http.SessionAsync())));
        ba.MapPost("/company", async (HttpContext http, CompanyBankAccountInput i, BankAccountService s) => Results.Ok(new { (await s.AddCompanyAccountAsync(await http.SessionAsync(), i)).Id }));
        ba.MapPut("/company/{id:guid}", async (HttpContext http, Guid id, BankActiveRequest r, BankAccountService s) => { await s.SetCompanyAccountActiveAsync(await http.SessionAsync(), id, r.Active); return Results.NoContent(); });

        var eq = app.MapGroup("/api/equity");
        eq.MapGet("/shareholders", async (HttpContext http, DateOnly asOf, EquityService s) => Results.Ok(await s.ShareholdersAsync(await http.SessionAsync(), asOf)));
        eq.MapPost("/shareholders", async (HttpContext http, ShareholderInput i, EquityService s) => Results.Ok(new { (await s.AddShareholderAsync(await http.SessionAsync(), i)).Id }));
        eq.MapPut("/shareholders/{id:guid}", async (HttpContext http, Guid id, ShareholderUpdateRequest r, EquityService s) => { await s.UpdateShareholderAsync(await http.SessionAsync(), id, r.CommittedCapital, r.IsIndividual, r.IsActive, r.Note); return Results.NoContent(); });
        eq.MapGet("/movements", async (HttpContext http, DateOnly? from, DateOnly? to, EquityService s) => Results.Ok(await s.MovementsAsync(await http.SessionAsync(), from, to)));
        eq.MapPost("/movements", async (HttpContext http, CapitalInput i, EquityService s) => { var v = await s.RecordMovementAsync(await http.SessionAsync(), i); return Results.Ok(new { v.Id, v.Number }); });
        eq.MapGet("/dividends", async (HttpContext http, EquityService s) => Results.Ok(await s.DividendsAsync(await http.SessionAsync())));
        eq.MapPost("/dividends", async (HttpContext http, DividendInput i, EquityService s) => Results.Ok(new { (await s.CreateDividendAsync(await http.SessionAsync(), i)).Id }));
        eq.MapGet("/dividends/{id:guid}", async (HttpContext http, Guid id, EquityService s) => Results.Ok(await s.DividendAsync(await http.SessionAsync(), id)));
        eq.MapPost("/dividends/{id:guid}/decide", async (HttpContext http, Guid id, ContractDecisionRequest r, EquityService s) => Results.Ok(new { (await s.DecideDividendAsync(await http.SessionAsync(), id, r.Approve, r.Note)).Id }));
        eq.MapPost("/dividends/{id:guid}/declare", async (HttpContext http, Guid id, DividendDeclareRequest r, EquityService s) => { var v = await s.DeclareAsync(await http.SessionAsync(), id, r.Date); return Results.Ok(new { v.Id, v.Number }); });
        eq.MapPost("/dividends/{id:guid}/pay", async (HttpContext http, Guid id, DividendPayRequest r, EquityService s) => { var v = await s.CreatePaymentAsync(await http.SessionAsync(), id, r.ShareholderId, r.CashAccountId, r.Date); return Results.Ok(new { v.Id, v.Number }); });
        eq.MapPost("/dividends/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, EquityService s) => { await s.CancelDividendAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
