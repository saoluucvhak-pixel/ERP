using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope = table.Column<int>(type: "INTEGER", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    price_percent = table.Column<long>(type: "INTEGER", nullable: false),
                    price_absolute = table.Column<long>(type: "INTEGER", nullable: false),
                    quantity_percent = table.Column<long>(type: "INTEGER", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    snapshot_hash = table.Column<string>(type: "TEXT", nullable: false),
                    snapshot_text = table.Column<string>(type: "TEXT", nullable: false),
                    policy_version = table.Column<int>(type: "INTEGER", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    approved_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    closed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    close_reason = table.Column<string>(type: "TEXT", nullable: true)
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
