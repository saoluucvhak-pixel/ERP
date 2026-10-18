using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "long_term_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    @class = table.Column<int>(name: "class", type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    asset_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accumulated_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    proration = table.Column<int>(type: "integer", nullable: false),
                    original_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    residual_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    useful_life_months = table.Column<int>(type: "integer", nullable: false),
                    in_service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_accumulated = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remaining_life_months = table.Column<int>(type: "integer", nullable: true),
                    schedule_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    disposal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    disposal_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_disposal = table.Column<bool>(type: "boolean", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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

            foreach (var sql in ImmutabilityTriggers.AssetStatements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.AssetDownStatements()) migrationBuilder.Sql(sql);
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
