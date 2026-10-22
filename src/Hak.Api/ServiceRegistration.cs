using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Banking;
using Hak.Application.Common;
using Hak.Application.Cycles;
using Hak.Application.Exchange;
using Hak.Application.Inventory;
using Hak.Application.Rules;
using Hak.Application.Settlement;
using Hak.Application.Security;
using Hak.Application.Setup;
using Hak.Application.Tax;
using Hak.Application.Trade;

namespace Hak.Api;

/// <summary>
/// Every request-scoped application service, in one place (Program.cs keeps only configuration-dependent singletons).
/// A new service is added here; ApiTests.EveryApiEndpoint_RequiresASession proves the endpoints that use it still need a login.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddHakApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<Hak.Application.Attachments.AttachmentService>();
        services.AddScoped<AuditService>();
        services.AddScoped<SetupService>();
        services.AddScoped<CatalogImportService>();
        services.AddScoped<AccountingEngine>();
        services.AddScoped<LedgerQueryService>();
        services.AddScoped<AuthService>();
        services.AddScoped<JournalVoucherPackageService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<PostingRuleService>();
        services.AddScoped<BusinessDocumentService>();
        services.AddScoped<ClosingService>();
        services.AddScoped<PaymentOrderService>();
        services.AddScoped<PaymentAcceptanceService>();
        services.AddScoped<PurchaseVarianceService>();
        services.AddScoped<PurchaseReceivingService>();
        services.AddScoped<OrderFlowService>();
        services.AddScoped<Hak.Application.Setup.PeriodCloseChecklistService>();
        services.AddScoped<SalesOrderCheckService>();
        services.AddScoped<ReorderService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<HomeService>();
        services.AddScoped<Hak.Application.Reports.FinancialAnalysisService>();
        services.AddScoped<Hak.Application.Reports.DashboardOverviewService>();
        services.AddScoped<PayrollPaymentService>();
        services.AddScoped<Hak.Application.Assets.AssetCycleService>();
        services.AddScoped<Hak.Application.Assets.AssetRepairService>();
        services.AddScoped<Hak.Application.Assets.AssetJournalService>();
        services.AddScoped<SalesDispatchService>();
        services.AddScoped<GoodsInspectionService>();
        services.AddScoped<PurchaseMatchService>();
        services.AddScoped<SalesReportService>();
        services.AddScoped<CashPlanService>();
        services.AddScoped<BankReconciliationService>();
        services.AddScoped<StatementVoucherService>();
        services.AddScoped<StockService>();
        services.AddScoped<DrynessService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<OrderService>();
        services.AddScoped<ContractService>();
        services.AddScoped<Hak.Application.Advances.AdvanceService>();
        services.AddScoped<SalesApprovalService>();
        services.AddScoped<ServiceAcceptanceService>();
        services.AddScoped<ServiceReportService>();
        services.AddScoped<Hak.Application.Accounting.SpecialJournalService>();
        services.AddScoped<ContractServicePlanService>();
        services.AddScoped<ServiceProgressService>();
        services.AddScoped<PaymentDiscountService>();
        services.AddScoped<NumberingService>();
        services.AddScoped<Hak.Application.Setup.GroupService>();
        services.AddScoped<Hak.Application.Banking.BankAccountService>();
        services.AddScoped<Hak.Application.Equity.EquityService>();
        services.AddScoped<Hak.Application.Advances.CashCountService>();
        services.AddScoped<VatService>();
        services.AddScoped<VatOffsetService>();
        services.AddScoped<Hak.Application.Assets.AssetService>();
        services.AddScoped<Hak.Application.Fx.FxService>();
        services.AddScoped<Hak.Application.Fx.FxPaymentService>();
        services.AddScoped<Hak.Application.Loans.LoanService>();
        services.AddScoped<Hak.Application.Loans.LoanScheduleService>();
        services.AddScoped<Hak.Application.Payroll.PayrollService>();
        services.AddScoped<Hak.Application.Exchange.ExchangeKeyService>();
        services.AddScoped<Hak.Application.Production.ProductionCostService>();
        services.AddScoped<Hak.Application.Production.ProductionOrderService>();
        services.AddScoped<Hak.Application.Backup.BackupService>();
        services.AddScoped<Hak.Application.Reports.ReportService>();
        return services;
    }
}
