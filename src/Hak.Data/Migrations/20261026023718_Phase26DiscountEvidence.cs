using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "amount_convention",
                table: "e_invoice_records",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "settlement_snapshot",
                table: "discount_agreement_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "adjustment_reason",
                table: "commercial_invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_no_delete BEFORE DELETE ON discount_agreement_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_snapshot_frozen BEFORE UPDATE ON discount_agreement_runs WHEN OLD.settlement_snapshot IS NOT NULL AND (OLD.settlement_snapshot IS NOT NEW.settlement_snapshot OR OLD.company_id IS NOT NEW.company_id OR OLD.agreement_id IS NOT NEW.agreement_id OR OLD.eligible_sales IS NOT NEW.eligible_sales OR OLD.percent IS NOT NEW.percent OR OLD.amount IS NOT NEW.amount OR OLD.accrued_amount IS NOT NEW.accrued_amount OR OLD.voucher_id IS NOT NEW.voucher_id OR OLD.accrual_reversal_voucher_id IS NOT NEW.accrual_reversal_voucher_id OR OLD.created_at IS NOT NEW.created_at OR OLD.created_by IS NOT NEW.created_by OR OLD.id IS NOT NEW.id) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_invoice_reason_frozen BEFORE UPDATE ON commercial_invoices WHEN OLD.status <> 1 AND OLD.adjustment_reason IS NOT NEW.adjustment_reason BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");

            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_no_update BEFORE UPDATE ON discount_agreement_run_invoices BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_no_delete BEFORE DELETE ON discount_agreement_run_invoices BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_frozen_insert BEFORE INSERT ON discount_agreement_run_invoices WHEN EXISTS (SELECT 1 FROM discount_agreement_runs r WHERE r.id = NEW.run_id AND r.settlement_snapshot IS NOT NULL) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_frozen_insert;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_runs_snapshot_frozen;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_runs_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_invoice_reason_frozen;");

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
