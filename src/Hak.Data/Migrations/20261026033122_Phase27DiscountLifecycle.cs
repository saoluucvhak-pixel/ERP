using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase27DiscountLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "accrual_account_id",
                table: "discount_agreement_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "accrual_settlement_mode",
                table: "discount_agreement_runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "recognition_account_id",
                table: "discount_agreement_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "accrued_amount",
                table: "discount_agreement_run_invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "discount_settlement_invoice_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adjustment_invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    received_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_settlement_invoice_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_discount_settlement_invoice_links_commercial_invoices_adjustment_invoice_id",
                        column: x => x.adjustment_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_settlement_invoice_links_commercial_invoices_original_invoice_id",
                        column: x => x.original_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_settlement_invoice_links_discount_agreement_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "discount_agreement_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discount_settlement_invoice_links_adjustment_invoice_id",
                table: "discount_settlement_invoice_links",
                column: "adjustment_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discount_settlement_invoice_links_original_invoice_id",
                table: "discount_settlement_invoice_links",
                column: "original_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_discount_settlement_invoice_links_run_id_original_invoice_id",
                table: "discount_settlement_invoice_links",
                columns: new[] { "run_id", "original_invoice_id" });

            migrationBuilder.Sql("DROP TRIGGER trg_discount_runs_snapshot_frozen;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_snapshot_frozen BEFORE UPDATE ON discount_agreement_runs WHEN OLD.settlement_snapshot IS NOT NULL AND (OLD.settlement_snapshot IS NOT NEW.settlement_snapshot OR OLD.company_id IS NOT NEW.company_id OR OLD.agreement_id IS NOT NEW.agreement_id OR OLD.eligible_sales IS NOT NEW.eligible_sales OR OLD.percent IS NOT NEW.percent OR OLD.amount IS NOT NEW.amount OR OLD.accrued_amount IS NOT NEW.accrued_amount OR OLD.voucher_id IS NOT NEW.voucher_id OR OLD.accrual_reversal_voucher_id IS NOT NEW.accrual_reversal_voucher_id OR OLD.accrual_settlement_mode IS NOT NEW.accrual_settlement_mode OR OLD.recognition_account_id IS NOT NEW.recognition_account_id OR OLD.accrual_account_id IS NOT NEW.accrual_account_id OR OLD.created_at IS NOT NEW.created_at OR OLD.created_by IS NOT NEW.created_by OR OLD.id IS NOT NEW.id) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_links_no_update BEFORE UPDATE ON discount_settlement_invoice_links BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_links_no_delete BEFORE DELETE ON discount_settlement_invoice_links BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Use native DROP COLUMN (SQLite >= 3.35). EF defers table rebuilds until after custom SQL, which would restore cross-table triggers too early.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_links_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_links_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_runs_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_runs_snapshot_frozen;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_discount_run_invoices_frozen_insert;");

            migrationBuilder.DropTable(
                name: "discount_settlement_invoice_links");

            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs DROP COLUMN accrual_account_id;");

            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs DROP COLUMN accrual_settlement_mode;");

            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs DROP COLUMN recognition_account_id;");

            migrationBuilder.Sql("ALTER TABLE discount_agreement_run_invoices DROP COLUMN accrued_amount;");

            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_no_delete BEFORE DELETE ON discount_agreement_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_snapshot_frozen BEFORE UPDATE ON discount_agreement_runs WHEN OLD.settlement_snapshot IS NOT NULL AND (OLD.settlement_snapshot IS NOT NEW.settlement_snapshot OR OLD.company_id IS NOT NEW.company_id OR OLD.agreement_id IS NOT NEW.agreement_id OR OLD.eligible_sales IS NOT NEW.eligible_sales OR OLD.percent IS NOT NEW.percent OR OLD.amount IS NOT NEW.amount OR OLD.accrued_amount IS NOT NEW.accrued_amount OR OLD.voucher_id IS NOT NEW.voucher_id OR OLD.accrual_reversal_voucher_id IS NOT NEW.accrual_reversal_voucher_id OR OLD.created_at IS NOT NEW.created_at OR OLD.created_by IS NOT NEW.created_by OR OLD.id IS NOT NEW.id) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_no_update BEFORE UPDATE ON discount_agreement_run_invoices BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_no_delete BEFORE DELETE ON discount_agreement_run_invoices BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_run_invoices_frozen_insert BEFORE INSERT ON discount_agreement_run_invoices WHEN EXISTS (SELECT 1 FROM discount_agreement_runs r WHERE r.id = NEW.run_id AND r.settlement_snapshot IS NOT NULL) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }
    }
}
