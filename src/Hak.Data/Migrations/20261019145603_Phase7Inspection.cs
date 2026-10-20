using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Inspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "goods_inspections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    stock_document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    inspection_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    inspector = table.Column<string>(type: "TEXT", nullable: false),
                    conclusion = table.Column<string>(type: "TEXT", nullable: true),
                    is_cancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    cancel_reason = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_inspections_stock_documents_stock_document_id",
                        column: x => x.stock_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_inspection_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inspection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    stock_line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    received_qty = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_qty = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_inspection_lines", x => x.id);
                    table.CheckConstraint("ck_goods_inspection_lines_qty", "accepted_qty >= 0 AND accepted_qty <= received_qty");
                    table.ForeignKey(
                        name: "fk_goods_inspection_lines_goods_inspections_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "goods_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_inspection_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goods_inspection_lines_inspection_id_stock_line_no",
                table: "goods_inspection_lines",
                columns: new[] { "inspection_id", "stock_line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_inspection_lines_item_id",
                table: "goods_inspection_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_inspections_company_id_number",
                table: "goods_inspections",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_inspections_company_id_stock_document_id",
                table: "goods_inspections",
                columns: new[] { "company_id", "stock_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_inspections_stock_document_id",
                table: "goods_inspections",
                column: "stock_document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_inspection_lines");

            migrationBuilder.DropTable(
                name: "goods_inspections");
        }
    }
}
