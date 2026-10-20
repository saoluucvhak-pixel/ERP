using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase9AllocationFrequency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "allocation_frequency",
                table: "long_term_assets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Kỳ ghi sổ khấu hao / phân bổ cũng đóng băng khi thẻ đã ghi tăng.
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
                   OR NEW.original_cost IS DISTINCT FROM OLD.original_cost OR NEW.residual_value IS DISTINCT FROM OLD.residual_value
                   OR NEW.useful_life_months IS DISTINCT FROM OLD.useful_life_months OR NEW.in_service_date IS DISTINCT FROM OLD.in_service_date
                   OR NEW.opening_accumulated IS DISTINCT FROM OLD.opening_accumulated OR NEW.remaining_life_months IS DISTINCT FROM OLD.remaining_life_months
                   OR NEW.schedule_start_date IS DISTINCT FROM OLD.schedule_start_date) THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            """);
            migrationBuilder.DropColumn(
                name: "allocation_frequency",
                table: "long_term_assets");
        }
    }
}
