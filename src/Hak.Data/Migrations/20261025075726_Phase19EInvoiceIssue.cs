using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase19EInvoiceIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "replaces_invoice_id",
                table: "commercial_invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "e_invoice_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    invoice_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    provider = table.Column<string>(type: "TEXT", nullable: false),
                    provider_ref = table.Column<string>(type: "TEXT", nullable: true),
                    template_code = table.Column<string>(type: "TEXT", nullable: false),
                    series = table.Column<string>(type: "TEXT", nullable: true),
                    number = table.Column<string>(type: "TEXT", nullable: true),
                    issue_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    signed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    tax_authority_code = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    relation = table.Column<int>(type: "INTEGER", nullable: false),
                    related_document_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    nature = table.Column<int>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    xml_content = table.Column<string>(type: "TEXT", nullable: true),
                    xml_sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    post_error = table.Column<string>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    requested_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    requested_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_error_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    provider_ref = table.Column<string>(type: "TEXT", nullable: true),
                    response = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    sent_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    responded_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_error_notices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    notice_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    payload_sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    code = table.Column<string>(type: "TEXT", nullable: true),
                    detail = table.Column<string>(type: "TEXT", nullable: true),
                    at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation = table.Column<string>(type: "TEXT", nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_outbox_e_invoice_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "e_invoice_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_error_notice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    notice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    error_description = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_error_notice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_error_notice_lines_e_invoice_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "e_invoice_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_e_invoice_error_notice_lines_e_invoice_error_notices_notice_id",
                        column: x => x.notice_id,
                        principalTable: "e_invoice_error_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_documents_company_id_invoice_id",
                table: "e_invoice_documents",
                columns: new[] { "company_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_documents_company_id_series_number",
                table: "e_invoice_documents",
                columns: new[] { "company_id", "series", "number" },
                unique: true,
                filter: "series IS NOT NULL AND number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_documents_company_id_status",
                table: "e_invoice_documents",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_error_notice_lines_document_id",
                table: "e_invoice_error_notice_lines",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_error_notice_lines_notice_id",
                table: "e_invoice_error_notice_lines",
                column: "notice_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_error_notices_company_id_number",
                table: "e_invoice_error_notices",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_messages_company_id_document_id_at",
                table: "e_invoice_messages",
                columns: new[] { "company_id", "document_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_outbox_company_id_state_next_attempt_at",
                table: "e_invoice_outbox",
                columns: new[] { "company_id", "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_outbox_document_id",
                table: "e_invoice_outbox",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_outbox_idempotency_key",
                table: "e_invoice_outbox",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e_invoice_error_notice_lines");

            migrationBuilder.DropTable(
                name: "e_invoice_messages");

            migrationBuilder.DropTable(
                name: "e_invoice_outbox");

            migrationBuilder.DropTable(
                name: "e_invoice_error_notices");

            migrationBuilder.DropTable(
                name: "e_invoice_documents");

            migrationBuilder.DropColumn(
                name: "replaces_invoice_id",
                table: "commercial_invoices");
        }
    }
}
