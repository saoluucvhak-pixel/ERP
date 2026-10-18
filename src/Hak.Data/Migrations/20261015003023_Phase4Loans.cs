using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    lender_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    principal_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    interest_expense_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    accrued_interest_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    contract_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    start_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    maturity_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    interest_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    day_count = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    activated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    activated_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    rates_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    reversed_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_interest_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    loan_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    loan_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    annual_rate_percent = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_rates", x => x.id);
                    table.CheckConstraint("ck_loan_rates_rate", "annual_rate_percent >= 0 AND annual_rate_percent <= 1000000");
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    loan_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    foreign_amount = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    voided_at = table.Column<DateTime>(type: "TEXT", nullable: true)
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

            // Immutability (docs/02 ADR-07): contracts freeze when active; rates, links and runs are append-only evidence.
            foreach (var t in Triggers) migrationBuilder.Sql(t.Create);
        }

        private static readonly (string Name, string Create)[] Triggers =
        [
            ("trg_loan_contracts_no_delete", "CREATE TRIGGER trg_loan_contracts_no_delete BEFORE DELETE ON loan_contracts BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_contracts_frozen", """
                CREATE TRIGGER trg_loan_contracts_frozen BEFORE UPDATE ON loan_contracts
                WHEN OLD.status <> 1 AND (NEW.company_id IS NOT OLD.company_id OR NEW.code IS NOT OLD.code OR NEW.lender_id IS NOT OLD.lender_id
                  OR NEW.principal_account_id IS NOT OLD.principal_account_id OR NEW.interest_expense_account_id IS NOT OLD.interest_expense_account_id
                  OR NEW.accrued_interest_account_id IS NOT OLD.accrued_interest_account_id OR NEW.currency IS NOT OLD.currency
                  OR NEW.contract_amount IS NOT OLD.contract_amount OR NEW.start_date IS NOT OLD.start_date OR NEW.maturity_date IS NOT OLD.maturity_date
                  OR NEW.interest_from IS NOT OLD.interest_from OR NEW.day_count IS NOT OLD.day_count OR NEW.description IS NOT OLD.description)
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """),
            ("trg_loan_rates_no_update", "CREATE TRIGGER trg_loan_rates_no_update BEFORE UPDATE ON loan_rates BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_rates_no_delete", "CREATE TRIGGER trg_loan_rates_no_delete BEFORE DELETE ON loan_rates BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_links_no_update", "CREATE TRIGGER trg_loan_links_no_update BEFORE UPDATE ON loan_links BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_links_no_delete", "CREATE TRIGGER trg_loan_links_no_delete BEFORE DELETE ON loan_links BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_interest_runs_no_delete", "CREATE TRIGGER trg_loan_interest_runs_no_delete BEFORE DELETE ON loan_interest_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_interest_entries_no_delete", "CREATE TRIGGER trg_loan_interest_entries_no_delete BEFORE DELETE ON loan_interest_entries BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_loan_interest_entries_void_once", """
                CREATE TRIGGER trg_loan_interest_entries_void_once BEFORE UPDATE ON loan_interest_entries
                WHEN OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.amount IS NOT OLD.amount OR NEW.foreign_amount IS NOT OLD.foreign_amount
                  OR NEW.loan_id IS NOT OLD.loan_id OR NEW.run_id IS NOT OLD.run_id OR NEW.period_end IS NOT OLD.period_end
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in Triggers) migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {t.Name};");
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
