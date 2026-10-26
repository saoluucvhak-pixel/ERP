using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "accrual_settlement_mode",
                table: "discount_agreement_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "recognition_account_id",
                table: "discount_agreement_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "accrued_amount",
                table: "discount_agreement_run_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "discount_settlement_invoice_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_settlement_invoice_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_discount_settlement_invoice_links_commercial_invoices_adjus~",
                        column: x => x.adjustment_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_settlement_invoice_links_commercial_invoices_origi~",
                        column: x => x.original_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_settlement_invoice_links_discount_agreement_runs_r~",
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

            migrationBuilder.Sql("CREATE TRIGGER trg_discount_links_append_only BEFORE UPDATE OR DELETE ON discount_settlement_invoice_links FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER trg_discount_links_append_only ON discount_settlement_invoice_links;");

            migrationBuilder.DropTable(
                name: "discount_settlement_invoice_links");

            migrationBuilder.DropColumn(
                name: "accrual_account_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "accrual_settlement_mode",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "recognition_account_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "accrued_amount",
                table: "discount_agreement_run_invoices");
        }
    }
}
