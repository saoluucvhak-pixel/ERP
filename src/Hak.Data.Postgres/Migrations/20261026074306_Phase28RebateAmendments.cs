using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_run_id",
                table: "discount_agreement_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                table: "discount_agreement_runs",
                type: "integer",
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

            migrationBuilder.AddForeignKey(
                name: "fk_discount_agreement_runs_discount_agreement_runs_parent_run_~",
                table: "discount_agreement_runs",
                column: "parent_run_id",
                principalTable: "discount_agreement_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION hak_rebate_chain_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'INSERT' AND NEW.parent_run_id IS NOT NULL AND NOT EXISTS
                    (SELECT 1 FROM discount_agreement_runs p WHERE p.id = NEW.parent_run_id AND p.company_id = NEW.company_id
                     AND p.agreement_id = NEW.agreement_id AND p.voided_at IS NULL AND p.settlement_snapshot IS NOT NULL AND p.sequence < NEW.sequence)
                  THEN RAISE EXCEPTION 'REBATE_PARENT_INVALID'; END IF;
                  IF TG_OP = 'UPDATE' AND NEW.voided_at IS NOT NULL AND EXISTS
                    (SELECT 1 FROM discount_agreement_runs c WHERE c.parent_run_id = OLD.id AND c.voided_at IS NULL)
                  THEN RAISE EXCEPTION 'REBATE_CHILD_ACTIVE'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_discount_runs_chain_guard BEFORE INSERT OR UPDATE ON discount_agreement_runs FOR EACH ROW EXECUTE FUNCTION hak_rebate_chain_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM discount_agreement_runs WHERE parent_run_id IS NOT NULL) THEN RAISE EXCEPTION 'REBATE_HISTORY_CANNOT_DOWNGRADE'; END IF; END $$;");
            migrationBuilder.Sql("DROP TRIGGER trg_discount_runs_chain_guard ON discount_agreement_runs; DROP FUNCTION hak_rebate_chain_guard();");
            migrationBuilder.DropForeignKey(
                name: "fk_discount_agreement_runs_discount_agreement_runs_parent_run_~",
                table: "discount_agreement_runs");

            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_agreement_id_sequence",
                table: "discount_agreement_runs");

            migrationBuilder.DropIndex(
                name: "ix_discount_agreement_runs_parent_run_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "amendment_reason",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "parent_run_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "discount_agreement_runs");

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs",
                column: "agreement_id",
                unique: true,
                filter: "voided_at IS NULL");
        }
    }
}
