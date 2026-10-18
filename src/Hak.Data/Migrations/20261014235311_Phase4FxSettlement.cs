using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4FxSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "foreign_amount",
                table: "settlement_allocations",
                type: "INTEGER",
                nullable: true);

            // The void-once guard now also freezes the foreign amount settled.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_settlement_allocations_void_once;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_settlement_allocations_void_once BEFORE UPDATE ON settlement_allocations
                WHEN OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL
                  OR NEW.amount IS NOT OLD.amount OR NEW.foreign_amount IS NOT OLD.foreign_amount
                  OR NEW.debit_line_id IS NOT OLD.debit_line_id OR NEW.credit_line_id IS NOT OLD.credit_line_id
                  OR NEW.allocation_date IS NOT OLD.allocation_date OR NEW.account_id IS NOT OLD.account_id OR NEW.partner_id IS NOT OLD.partner_id
                  OR NEW.company_id IS NOT OLD.company_id
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "foreign_amount",
                table: "settlement_allocations");
        }
    }
}
