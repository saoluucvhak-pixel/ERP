using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Vat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "e_invoice_import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_name = table.Column<string>(type: "TEXT", nullable: false),
                    source_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    imported = table.Column<int>(type: "INTEGER", nullable: false),
                    duplicates = table.Column<int>(type: "INTEGER", nullable: false),
                    imported_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    imported_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_declarations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tax_type = table.Column<string>(type: "TEXT", nullable: false),
                    period_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_to = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    filing_reference = table.Column<string>(type: "TEXT", nullable: true),
                    filed_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_declarations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    direction = table.Column<int>(type: "INTEGER", nullable: false),
                    seller_tax_code = table.Column<string>(type: "TEXT", nullable: false),
                    seller_name = table.Column<string>(type: "TEXT", nullable: false),
                    buyer_tax_code = table.Column<string>(type: "TEXT", nullable: true),
                    buyer_name = table.Column<string>(type: "TEXT", nullable: true),
                    series = table.Column<string>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    amount_before_vat = table.Column<long>(type: "INTEGER", nullable: false),
                    vat_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    total_payment = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    related_series = table.Column<string>(type: "TEXT", nullable: true),
                    related_number = table.Column<string>(type: "TEXT", nullable: true),
                    lookup_code = table.Column<string>(type: "TEXT", nullable: true),
                    import_batch_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    imported_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    imported_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_records_e_invoice_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalTable: "e_invoice_import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_declaration_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    declaration_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    indicator_code = table.Column<string>(type: "TEXT", nullable: false),
                    label = table.Column<string>(type: "TEXT", nullable: false),
                    declared_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    measure = table.Column<int>(type: "INTEGER", nullable: false),
                    vat_rate = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_declaration_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_declaration_lines_tax_declarations_declaration_id",
                        column: x => x.declaration_id,
                        principalTable: "tax_declarations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_rate_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    e_invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    vat_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    amount_before_vat = table.Column<long>(type: "INTEGER", nullable: false),
                    vat_amount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_rate_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_rate_lines_e_invoice_records_e_invoice_id",
                        column: x => x.e_invoice_id,
                        principalTable: "e_invoice_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    e_invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    new_status = table.Column<int>(type: "INTEGER", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_status_changes_e_invoice_records_e_invoice_id",
                        column: x => x.e_invoice_id,
                        principalTable: "e_invoice_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_import_batches_company_id_source_sha256",
                table: "e_invoice_import_batches",
                columns: new[] { "company_id", "source_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_rate_lines_e_invoice_id",
                table: "e_invoice_rate_lines",
                column: "e_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_records_company_id_direction_seller_tax_code_series_number",
                table: "e_invoice_records",
                columns: new[] { "company_id", "direction", "seller_tax_code", "series", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_records_company_id_issue_date",
                table: "e_invoice_records",
                columns: new[] { "company_id", "issue_date" });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_records_import_batch_id",
                table: "e_invoice_records",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_status_changes_e_invoice_id_at",
                table: "e_invoice_status_changes",
                columns: new[] { "e_invoice_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_declaration_lines_declaration_id_indicator_code",
                table: "tax_declaration_lines",
                columns: new[] { "declaration_id", "indicator_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_declarations_company_id_tax_type_period_from_period_to",
                table: "tax_declarations",
                columns: new[] { "company_id", "tax_type", "period_from", "period_to" },
                unique: true);

            // Imported e-invoice data is evidence for the 3-way reconciliation: never edited or deleted.
            foreach (var t in new[] { "e_invoice_records", "e_invoice_rate_lines", "e_invoice_import_batches", "e_invoice_status_changes" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_update BEFORE UPDATE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "e_invoice_records", "e_invoice_rate_lines", "e_invoice_import_batches", "e_invoice_status_changes" })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_update;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_delete;");
            }
            migrationBuilder.DropTable(
                name: "e_invoice_rate_lines");

            migrationBuilder.DropTable(
                name: "e_invoice_status_changes");

            migrationBuilder.DropTable(
                name: "tax_declaration_lines");

            migrationBuilder.DropTable(
                name: "e_invoice_records");

            migrationBuilder.DropTable(
                name: "tax_declarations");

            migrationBuilder.DropTable(
                name: "e_invoice_import_batches");
        }
    }
}
