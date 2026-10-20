using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7PaymentAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    settle_journal_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    payment_order_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    accepted_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    closed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    close_reason = table.Column<string>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_acceptances", x => x.id);
                    table.CheckConstraint("ck_payment_acceptances_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_acceptances_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_acceptances_commercial_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_acceptances_company_id_invoice_id",
                table: "payment_acceptances",
                columns: new[] { "company_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_acceptances_company_id_status",
                table: "payment_acceptances",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_acceptances_invoice_id",
                table: "payment_acceptances",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_acceptances_partner_id",
                table: "payment_acceptances",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_acceptances");
        }
    }
}
