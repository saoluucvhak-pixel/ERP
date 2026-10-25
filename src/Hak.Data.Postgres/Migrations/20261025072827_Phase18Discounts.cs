using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase18Discounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discount_percent",
                table: "trade_order_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "discount_kind",
                table: "commercial_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "commercial_invoice_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_percent",
                table: "commercial_invoice_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_discount_amount",
                table: "commercial_invoice_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "discount_agreements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_agreements", x => x.id);
                    table.CheckConstraint("ck_discount_agreements_dates", "from_date <= to_date");
                    table.ForeignKey(
                        name: "fk_discount_agreements_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discount_agreement_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eligible_sales = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_agreement_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_discount_agreement_runs_discount_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalTable: "discount_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discount_agreement_tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    threshold = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_agreement_tiers", x => x.id);
                    table.CheckConstraint("ck_discount_agreement_tiers_values", "threshold >= 0 AND percent > 0 AND percent <= 100");
                    table.ForeignKey(
                        name: "fk_discount_agreement_tiers_discount_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalTable: "discount_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discount_agreement_run_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_agreement_run_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_discount_agreement_run_invoices_commercial_invoices_adjustm~",
                        column: x => x.adjustment_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_agreement_run_invoices_commercial_invoices_origina~",
                        column: x => x.original_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_agreement_run_invoices_discount_agreement_runs_run~",
                        column: x => x.run_id,
                        principalTable: "discount_agreement_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_run_invoices_adjustment_invoice_id",
                table: "discount_agreement_run_invoices",
                column: "adjustment_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_run_invoices_original_invoice_id",
                table: "discount_agreement_run_invoices",
                column: "original_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_run_invoices_run_id",
                table: "discount_agreement_run_invoices",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_runs_agreement_id",
                table: "discount_agreement_runs",
                column: "agreement_id",
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreement_tiers_agreement_id_threshold",
                table: "discount_agreement_tiers",
                columns: new[] { "agreement_id", "threshold" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreements_company_id_code",
                table: "discount_agreements",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreements_company_id_kind_partner_id",
                table: "discount_agreements",
                columns: new[] { "company_id", "kind", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_discount_agreements_partner_id",
                table: "discount_agreements",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discount_agreement_run_invoices");

            migrationBuilder.DropTable(
                name: "discount_agreement_tiers");

            migrationBuilder.DropTable(
                name: "discount_agreement_runs");

            migrationBuilder.DropTable(
                name: "discount_agreements");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                table: "trade_order_lines");

            migrationBuilder.DropColumn(
                name: "discount_kind",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "commercial_invoice_lines");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                table: "commercial_invoice_lines");

            migrationBuilder.DropColumn(
                name: "functional_discount_amount",
                table: "commercial_invoice_lines");
        }
    }
}
