using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase28RebateAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs");

            migrationBuilder.AddColumn<string>(
                name: "amendment_reason",
                table: "discount_agreement_runs",
                type: "TEXT",
                nullable: true);

            // Native ADD COLUMN supports a nullable self-reference without rebuilding the table and losing its immutable triggers.
            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs ADD COLUMN parent_run_id TEXT CONSTRAINT fk_discount_agreement_runs_discount_agreement_runs_parent_run_id REFERENCES discount_agreement_runs(id) ON DELETE RESTRICT;");

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                table: "discount_agreement_runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs",
                column: "agreement_id",
                unique: true,
                filter: "voided_at IS NULL AND parent_run_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_runs_agreement_id_sequence",
                table: "discount_agreement_runs",
                columns: new[] { "agreement_id", "sequence" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_runs_parent_run_id",
                table: "discount_agreement_runs",
                column: "parent_run_id",
                unique: true,
                filter: "voided_at IS NULL AND parent_run_id IS NOT NULL");

            migrationBuilder.Sql("DROP TRIGGER trg_discount_runs_snapshot_frozen;");
            migrationBuilder.Sql(FrozenTrigger(true));
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_parent_guard BEFORE INSERT ON discount_agreement_runs WHEN NEW.parent_run_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM discount_agreement_runs p WHERE p.id = NEW.parent_run_id AND p.company_id = NEW.company_id AND p.agreement_id = NEW.agreement_id AND p.voided_at IS NULL AND p.settlement_snapshot IS NOT NULL AND p.sequence < NEW.sequence) BEGIN SELECT RAISE(ABORT, 'REBATE_PARENT_INVALID'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_discount_runs_parent_void BEFORE UPDATE OF voided_at ON discount_agreement_runs WHEN NEW.voided_at IS NOT NULL AND EXISTS (SELECT 1 FROM discount_agreement_runs c WHERE c.parent_run_id = OLD.id AND c.voided_at IS NULL) BEGIN SELECT RAISE(ABORT, 'REBATE_CHILD_ACTIVE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A previous binary cannot understand amendment history. Refuse downgrade instead of erasing parentage.
            migrationBuilder.Sql("CREATE TABLE __rebate_downgrade_guard (id INTEGER CHECK(id = 0)); INSERT INTO __rebate_downgrade_guard SELECT 1 WHERE EXISTS (SELECT 1 FROM discount_agreement_runs WHERE parent_run_id IS NOT NULL); DROP TABLE __rebate_downgrade_guard;");
            migrationBuilder.Sql("DROP TRIGGER trg_discount_runs_parent_guard;");
            migrationBuilder.Sql("DROP TRIGGER trg_discount_runs_parent_void;");
            migrationBuilder.Sql("DROP TRIGGER trg_discount_runs_snapshot_frozen;");

            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_agreement_id_sequence",
                table: "discount_agreement_runs");

            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_parent_run_id",
                table: "discount_agreement_runs");

            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs DROP COLUMN amendment_reason;");
            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs DROP COLUMN parent_run_id;");
            migrationBuilder.Sql("ALTER TABLE discount_agreement_runs DROP COLUMN sequence;");
            migrationBuilder.Sql(FrozenTrigger(false));

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs",
                column: "agreement_id",
                unique: true,
                filter: "voided_at IS NULL");
        }

        private static string FrozenTrigger(bool amendment) => "CREATE TRIGGER trg_discount_runs_snapshot_frozen BEFORE UPDATE ON discount_agreement_runs WHEN OLD.settlement_snapshot IS NOT NULL AND (OLD.settlement_snapshot IS NOT NEW.settlement_snapshot OR OLD.company_id IS NOT NEW.company_id OR OLD.agreement_id IS NOT NEW.agreement_id OR OLD.eligible_sales IS NOT NEW.eligible_sales OR OLD.percent IS NOT NEW.percent OR OLD.amount IS NOT NEW.amount OR OLD.accrued_amount IS NOT NEW.accrued_amount OR OLD.voucher_id IS NOT NEW.voucher_id OR OLD.accrual_reversal_voucher_id IS NOT NEW.accrual_reversal_voucher_id OR OLD.accrual_settlement_mode IS NOT NEW.accrual_settlement_mode OR OLD.recognition_account_id IS NOT NEW.recognition_account_id OR OLD.accrual_account_id IS NOT NEW.accrual_account_id OR OLD.created_at IS NOT NEW.created_at OR OLD.created_by IS NOT NEW.created_by OR OLD.id IS NOT NEW.id" + (amendment ? " OR OLD.parent_run_id IS NOT NEW.parent_run_id OR OLD.sequence IS NOT NEW.sequence OR OLD.amendment_reason IS NOT NEW.amendment_reason" : "") + ") BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;";
    }
}
