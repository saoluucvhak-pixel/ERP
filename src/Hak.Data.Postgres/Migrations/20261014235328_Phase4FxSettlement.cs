using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase4FxSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "foreign_amount",
                table: "settlement_allocations",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            // The void-once guard now also freezes the foreign amount settled.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION hak_settlement_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL
                     OR NEW.amount IS DISTINCT FROM OLD.amount OR NEW.foreign_amount IS DISTINCT FROM OLD.foreign_amount
                     OR NEW.debit_line_id IS DISTINCT FROM OLD.debit_line_id OR NEW.credit_line_id IS DISTINCT FROM OLD.credit_line_id
                     OR NEW.allocation_date IS DISTINCT FROM OLD.allocation_date OR NEW.account_id IS DISTINCT FROM OLD.account_id
                     OR NEW.partner_id IS DISTINCT FROM OLD.partner_id OR NEW.company_id IS DISTINCT FROM OLD.company_id THEN
                    RAISE EXCEPTION 'LEDGER_IMMUTABLE';
                  END IF;
                  RETURN NEW;
                END $$;
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
