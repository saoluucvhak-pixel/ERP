using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase25RiskNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "e_invoice_risk_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    authority_notice_no = table.Column<string>(type: "text", nullable: false),
                    notice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    response_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    response_note = table.Column<string>(type: "text", nullable: true),
                    response_notice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    responded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_risk_notices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_risk_notice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series = table.Column<string>(type: "text", nullable: false),
                    invoice_no = table.Column<string>(type: "text", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    issue = table.Column<string>(type: "text", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_risk_notice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_risk_notice_lines_e_invoice_risk_notices_notice_id",
                        column: x => x.notice_id,
                        principalTable: "e_invoice_risk_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_risk_notice_lines_notice_id",
                table: "e_invoice_risk_notice_lines",
                column: "notice_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_risk_notices_company_id_authority_notice_no",
                table: "e_invoice_risk_notices",
                columns: new[] { "company_id", "authority_notice_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_risk_notices_company_id_number",
                table: "e_invoice_risk_notices",
                columns: new[] { "company_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e_invoice_risk_notice_lines");

            migrationBuilder.DropTable(
                name: "e_invoice_risk_notices");
        }
    }
}
