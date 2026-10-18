using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Loans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loan_contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    lender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interest_expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accrued_interest_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    contract_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    maturity_date = table.Column<DateOnly>(type: "date", nullable: false),
                    interest_from = table.Column<DateOnly>(type: "date", nullable: false),
                    day_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_contracts", x => x.id);
                    table.CheckConstraint("ck_loan_contracts_dates", "maturity_date >= start_date AND contract_amount > 0");
                    table.ForeignKey(
                        name: "fk_loan_contracts_accounts_accrued_interest_account_id",
                        column: x => x.accrued_interest_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_contracts_accounts_interest_expense_account_id",
                        column: x => x.interest_expense_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_contracts_accounts_principal_account_id",
                        column: x => x.principal_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_contracts_business_partners_lender_id",
                        column: x => x.lender_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loan_interest_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rates_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_interest_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_links_journal_lines_journal_line_id",
                        column: x => x.journal_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_links_loan_contracts_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loan_contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loan_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    annual_rate_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_rates", x => x.id);
                    table.CheckConstraint("ck_loan_rates_rate", "annual_rate_percent >= 0 AND annual_rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_loan_rates_loan_contracts_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loan_contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loan_interest_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    foreign_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_interest_entries", x => x.id);
                    table.CheckConstraint("ck_loan_interest_entries_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_loan_interest_entries_loan_contracts_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loan_contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_interest_entries_loan_interest_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "loan_interest_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_contracts_accrued_interest_account_id",
                table: "loan_contracts",
                column: "accrued_interest_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_contracts_company_id_code",
                table: "loan_contracts",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_contracts_interest_expense_account_id",
                table: "loan_contracts",
                column: "interest_expense_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_contracts_lender_id",
                table: "loan_contracts",
                column: "lender_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_contracts_principal_account_id",
                table: "loan_contracts",
                column: "principal_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_interest_entries_company_id_loan_id",
                table: "loan_interest_entries",
                columns: new[] { "company_id", "loan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_interest_entries_loan_id",
                table: "loan_interest_entries",
                column: "loan_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_interest_entries_run_id",
                table: "loan_interest_entries",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_interest_runs_company_id_period_id",
                table: "loan_interest_runs",
                columns: new[] { "company_id", "period_id" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_loan_interest_runs_company_id_period_start",
                table: "loan_interest_runs",
                columns: new[] { "company_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_links_company_id_journal_line_id",
                table: "loan_links",
                columns: new[] { "company_id", "journal_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_links_journal_line_id",
                table: "loan_links",
                column: "journal_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_links_loan_id",
                table: "loan_links",
                column: "loan_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_rates_loan_id_effective_from",
                table: "loan_rates",
                columns: new[] { "loan_id", "effective_from" },
                unique: true);

            foreach (var sql in ImmutabilityTriggers.LoanStatements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.LoanDownStatements()) migrationBuilder.Sql(sql);
            migrationBuilder.DropTable(
                name: "loan_interest_entries");

            migrationBuilder.DropTable(
                name: "loan_links");

            migrationBuilder.DropTable(
                name: "loan_rates");

            migrationBuilder.DropTable(
                name: "loan_interest_runs");

            migrationBuilder.DropTable(
                name: "loan_contracts");
        }
    }
}
