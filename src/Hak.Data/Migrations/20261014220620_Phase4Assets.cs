using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Assets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "depreciation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    reversed_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "long_term_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    @class = table.Column<int>(name: "class", type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    asset_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    accumulated_account_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    expense_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    method = table.Column<int>(type: "INTEGER", nullable: false),
                    proration = table.Column<int>(type: "INTEGER", nullable: false),
                    original_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    residual_value = table.Column<long>(type: "INTEGER", nullable: false),
                    useful_life_months = table.Column<int>(type: "INTEGER", nullable: false),
                    in_service_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    opening_accumulated = table.Column<long>(type: "INTEGER", nullable: false),
                    remaining_life_months = table.Column<int>(type: "INTEGER", nullable: true),
                    schedule_start_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    disposal_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    disposal_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    activated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    activated_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_long_term_assets", x => x.id);
                    table.CheckConstraint("ck_long_term_assets_amounts", "original_cost >= 0 AND residual_value >= 0 AND opening_accumulated >= 0");
                    table.CheckConstraint("ck_long_term_assets_life", "useful_life_months > 0 AND (remaining_life_months IS NULL OR remaining_life_months > 0)");
                    table.ForeignKey(
                        name: "fk_long_term_assets_accounts_accumulated_account_id",
                        column: x => x.accumulated_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_long_term_assets_accounts_asset_account_id",
                        column: x => x.asset_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_long_term_assets_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_cost_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_cost_sources", x => x.id);
                    table.CheckConstraint("ck_asset_cost_sources_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_asset_cost_sources_journal_lines_journal_line_id",
                        column: x => x.journal_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_cost_sources_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "depreciation_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    is_disposal = table.Column<bool>(type: "INTEGER", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    voided_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_entries", x => x.id);
                    table.CheckConstraint("ck_depreciation_entries_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_depreciation_entries_depreciation_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "depreciation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_depreciation_entries_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_cost_sources_asset_id_journal_line_id",
                table: "asset_cost_sources",
                columns: new[] { "asset_id", "journal_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_cost_sources_company_id_journal_line_id",
                table: "asset_cost_sources",
                columns: new[] { "company_id", "journal_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_cost_sources_journal_line_id",
                table: "asset_cost_sources",
                column: "journal_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_entries_asset_id",
                table: "depreciation_entries",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_entries_company_id_asset_id",
                table: "depreciation_entries",
                columns: new[] { "company_id", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_entries_run_id",
                table: "depreciation_entries",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_company_id_period_id",
                table: "depreciation_runs",
                columns: new[] { "company_id", "period_id" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_company_id_period_start",
                table: "depreciation_runs",
                columns: new[] { "company_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_long_term_assets_accumulated_account_id",
                table: "long_term_assets",
                column: "accumulated_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_long_term_assets_asset_account_id",
                table: "long_term_assets",
                column: "asset_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_long_term_assets_company_id_code",
                table: "long_term_assets",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_long_term_assets_company_id_status",
                table: "long_term_assets",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_long_term_assets_expense_account_id",
                table: "long_term_assets",
                column: "expense_account_id");

            // Immutability (docs/02 ADR-07): asset parameters freeze at activation; cost sources and booked depreciation are evidence.
            migrationBuilder.Sql("CREATE TRIGGER trg_long_term_assets_no_delete BEFORE DELETE ON long_term_assets BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_long_term_assets_frozen BEFORE UPDATE ON long_term_assets
                WHEN OLD.status <> 1 AND (NEW.company_id IS NOT OLD.company_id OR NEW.code IS NOT OLD.code OR NEW.class IS NOT OLD.class
                  OR NEW.asset_account_id IS NOT OLD.asset_account_id OR NEW.accumulated_account_id IS NOT OLD.accumulated_account_id
                  OR NEW.expense_account_id IS NOT OLD.expense_account_id OR NEW.method IS NOT OLD.method OR NEW.proration IS NOT OLD.proration
                  OR NEW.original_cost IS NOT OLD.original_cost OR NEW.residual_value IS NOT OLD.residual_value
                  OR NEW.useful_life_months IS NOT OLD.useful_life_months OR NEW.in_service_date IS NOT OLD.in_service_date
                  OR NEW.opening_accumulated IS NOT OLD.opening_accumulated OR NEW.remaining_life_months IS NOT OLD.remaining_life_months
                  OR NEW.schedule_start_date IS NOT OLD.schedule_start_date)
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_asset_cost_sources_no_update BEFORE UPDATE ON asset_cost_sources BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_asset_cost_sources_no_delete BEFORE DELETE ON asset_cost_sources BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_depreciation_runs_no_delete BEFORE DELETE ON depreciation_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_depreciation_entries_no_delete BEFORE DELETE ON depreciation_entries BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_depreciation_entries_void_once BEFORE UPDATE ON depreciation_entries
                WHEN OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.amount IS NOT OLD.amount OR NEW.asset_id IS NOT OLD.asset_id
                  OR NEW.run_id IS NOT OLD.run_id OR NEW.is_disposal IS NOT OLD.is_disposal OR NEW.period_end IS NOT OLD.period_end
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "trg_long_term_assets_no_delete", "trg_long_term_assets_frozen", "trg_asset_cost_sources_no_update", "trg_asset_cost_sources_no_delete",
                         "trg_depreciation_runs_no_delete", "trg_depreciation_entries_no_delete", "trg_depreciation_entries_void_once" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {t};");
            migrationBuilder.DropTable(
                name: "asset_cost_sources");

            migrationBuilder.DropTable(
                name: "depreciation_entries");

            migrationBuilder.DropTable(
                name: "depreciation_runs");

            migrationBuilder.DropTable(
                name: "long_term_assets");
        }
    }
}
