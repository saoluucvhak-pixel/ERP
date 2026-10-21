using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase13PurchaseMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_match_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    price_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    price_absolute = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    quantity_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_match_policies", x => x.id);
                    table.CheckConstraint("ck_purchase_match_policies_tolerance", "price_percent >= 0 AND price_absolute >= 0 AND quantity_percent >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_match_policies_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_variance_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_hash = table.Column<string>(type: "text", nullable: false),
                    snapshot_text = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_variance_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_variance_approvals_commercial_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_match_policies_company_id_scope_partner_id_version",
                table: "purchase_match_policies",
                columns: new[] { "company_id", "scope", "partner_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_match_policies_partner_id",
                table: "purchase_match_policies",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_variance_approvals_company_id_invoice_id_status",
                table: "purchase_variance_approvals",
                columns: new[] { "company_id", "invoice_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_variance_approvals_invoice_id",
                table: "purchase_variance_approvals",
                column: "invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_match_policies");

            migrationBuilder.DropTable(
                name: "purchase_variance_approvals");
        }
    }
}
