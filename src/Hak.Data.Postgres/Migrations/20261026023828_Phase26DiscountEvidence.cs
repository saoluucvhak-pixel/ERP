using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase26DiscountEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "adjustment_nature",
                table: "e_invoice_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "amount_convention",
                table: "e_invoice_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "settlement_snapshot",
                table: "discount_agreement_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "adjustment_reason",
                table: "commercial_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_no_delete BEFORE DELETE ON discount_agreement_runs FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
            migrationBuilder.Sql("""
                CREATE FUNCTION hak_discount_run_snapshot_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.settlement_snapshot IS NOT NULL AND
                     (to_jsonb(OLD) - 'voided_at' - 'voided_by') IS DISTINCT FROM (to_jsonb(NEW) - 'voided_at' - 'voided_by')
                  THEN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_discount_runs_snapshot_frozen BEFORE UPDATE ON discount_agreement_runs FOR EACH ROW EXECUTE FUNCTION hak_discount_run_snapshot_frozen();
                CREATE FUNCTION hak_invoice_reason_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.status <> 1 AND OLD.adjustment_reason IS DISTINCT FROM NEW.adjustment_reason
                  THEN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_invoice_reason_frozen BEFORE UPDATE ON commercial_invoices FOR EACH ROW EXECUTE FUNCTION hak_invoice_reason_frozen();
                """);

            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_append_only BEFORE UPDATE OR DELETE ON discount_agreement_run_invoices FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
            migrationBuilder.Sql("""
                CREATE FUNCTION hak_discount_run_invoice_frozen_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM discount_agreement_runs r WHERE r.id = NEW.run_id AND r.settlement_snapshot IS NOT NULL)
                  THEN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_discount_run_invoices_frozen_insert BEFORE INSERT ON discount_agreement_run_invoices FOR EACH ROW EXECUTE FUNCTION hak_discount_run_invoice_frozen_insert();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_append_only ON discount_agreement_run_invoices;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_frozen_insert ON discount_agreement_run_invoices;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hak_discount_run_invoice_frozen_insert();");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_runs_snapshot_frozen ON discount_agreement_runs;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_runs_no_delete ON discount_agreement_runs;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_invoice_reason_frozen ON commercial_invoices;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hak_discount_run_snapshot_frozen(); DROP FUNCTION IF EXISTS hak_invoice_reason_frozen();");

            migrationBuilder.DropColumn(
                name: "adjustment_nature",
                table: "e_invoice_records");

            migrationBuilder.DropColumn(
                name: "amount_convention",
                table: "e_invoice_records");

            migrationBuilder.DropColumn(
                name: "settlement_snapshot",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "adjustment_reason",
                table: "commercial_invoices");
        }
    }
}
