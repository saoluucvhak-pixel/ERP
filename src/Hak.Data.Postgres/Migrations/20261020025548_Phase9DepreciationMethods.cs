using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase9DepreciationMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "declining_factor",
                table: "long_term_assets",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "schedule_end_date",
                table: "long_term_assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_units",
                table: "long_term_assets",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_name",
                table: "long_term_assets",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_usages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_usages_accounting_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_usages_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_usages_asset_id_period_id",
                table: "asset_usages",
                columns: new[] { "asset_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_usages_period_id",
                table: "asset_usages",
                column: "period_id");

            // Các thông số phương pháp đóng băng khi thẻ đã ghi tăng; kỳ ghi sổ (allocation_frequency) được đổi nên bỏ khỏi danh sách đóng băng.
            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION hak_long_term_assets_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.status <> 1 AND (NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.code IS DISTINCT FROM OLD.code
                   OR NEW.class IS DISTINCT FROM OLD.class OR NEW.asset_account_id IS DISTINCT FROM OLD.asset_account_id
                   OR NEW.accumulated_account_id IS DISTINCT FROM OLD.accumulated_account_id OR NEW.expense_account_id IS DISTINCT FROM OLD.expense_account_id
                   OR NEW.method IS DISTINCT FROM OLD.method OR NEW.proration IS DISTINCT FROM OLD.proration
                   OR NEW.schedule_end_date IS DISTINCT FROM OLD.schedule_end_date OR NEW.declining_factor IS DISTINCT FROM OLD.declining_factor
                   OR NEW.total_units IS DISTINCT FROM OLD.total_units OR NEW.unit_name IS DISTINCT FROM OLD.unit_name
                   OR NEW.original_cost IS DISTINCT FROM OLD.original_cost OR NEW.residual_value IS DISTINCT FROM OLD.residual_value
                   OR NEW.useful_life_months IS DISTINCT FROM OLD.useful_life_months OR NEW.in_service_date IS DISTINCT FROM OLD.in_service_date
                   OR NEW.opening_accumulated IS DISTINCT FROM OLD.opening_accumulated OR NEW.remaining_life_months IS DISTINCT FROM OLD.remaining_life_months
                   OR NEW.schedule_start_date IS DISTINCT FROM OLD.schedule_start_date) THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION hak_long_term_assets_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.status <> 1 AND (NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.code IS DISTINCT FROM OLD.code
                   OR NEW.class IS DISTINCT FROM OLD.class OR NEW.asset_account_id IS DISTINCT FROM OLD.asset_account_id
                   OR NEW.accumulated_account_id IS DISTINCT FROM OLD.accumulated_account_id OR NEW.expense_account_id IS DISTINCT FROM OLD.expense_account_id
                   OR NEW.method IS DISTINCT FROM OLD.method OR NEW.proration IS DISTINCT FROM OLD.proration
                   OR NEW.allocation_frequency IS DISTINCT FROM OLD.allocation_frequency
                   OR NEW.original_cost IS DISTINCT FROM OLD.original_cost OR NEW.residual_value IS DISTINCT FROM OLD.residual_value
                   OR NEW.useful_life_months IS DISTINCT FROM OLD.useful_life_months OR NEW.in_service_date IS DISTINCT FROM OLD.in_service_date
                   OR NEW.opening_accumulated IS DISTINCT FROM OLD.opening_accumulated OR NEW.remaining_life_months IS DISTINCT FROM OLD.remaining_life_months
                   OR NEW.schedule_start_date IS DISTINCT FROM OLD.schedule_start_date) THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            """);
            migrationBuilder.DropTable(
                name: "asset_usages");

            migrationBuilder.DropColumn(
                name: "declining_factor",
                table: "long_term_assets");

            migrationBuilder.DropColumn(
                name: "schedule_end_date",
                table: "long_term_assets");

            migrationBuilder.DropColumn(
                name: "total_units",
                table: "long_term_assets");

            migrationBuilder.DropColumn(
                name: "unit_name",
                table: "long_term_assets");
        }
    }
}
