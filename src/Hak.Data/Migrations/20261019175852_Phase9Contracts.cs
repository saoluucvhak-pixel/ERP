using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9Contracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "contract_id",
                table: "trade_orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    category = table.Column<int>(type: "INTEGER", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    exchange_rate = table.Column<long>(type: "INTEGER", nullable: false),
                    signed_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    payment_terms = table.Column<string>(type: "TEXT", nullable: true),
                    total_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    tax_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    status_reason = table.Column<string>(type: "TEXT", nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    submitted_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    approved_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contracts_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contract_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    amendment_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    value_change = table.Column<long>(type: "INTEGER", nullable: false),
                    new_expiration_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    line_changes_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_amendments", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_amendments_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contract_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    file_name = table.Column<string>(type: "TEXT", nullable: false),
                    file_path = table.Column<string>(type: "TEXT", nullable: false),
                    file_type = table.Column<string>(type: "TEXT", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_documents_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contract_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    committed_qty = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_price = table.Column<long>(type: "INTEGER", nullable: false),
                    discount_rate = table.Column<long>(type: "INTEGER", nullable: false),
                    tax_rate = table.Column<long>(type: "INTEGER", nullable: false),
                    notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_lines_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contract_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    percentage = table.Column<long>(type: "INTEGER", nullable: true),
                    due_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    paid_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    last_paid_date = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_milestones_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_contract_id",
                table: "trade_orders",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_amendments_company_id_code",
                table: "contract_amendments",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_amendments_contract_id",
                table: "contract_amendments",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_documents_contract_id",
                table: "contract_documents",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_lines_contract_id_item_id",
                table: "contract_lines",
                columns: new[] { "contract_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_lines_item_id",
                table: "contract_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_milestones_contract_id",
                table: "contract_milestones",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_company_id_code",
                table: "contracts",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_company_id_status",
                table: "contracts",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_partner_id",
                table: "contracts",
                column: "partner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_trade_orders_contracts_contract_id",
                table: "trade_orders",
                column: "contract_id",
                principalTable: "contracts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trade_orders_contracts_contract_id",
                table: "trade_orders");

            migrationBuilder.DropTable(
                name: "contract_amendments");

            migrationBuilder.DropTable(
                name: "contract_documents");

            migrationBuilder.DropTable(
                name: "contract_lines");

            migrationBuilder.DropTable(
                name: "contract_milestones");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_trade_orders_contract_id",
                table: "trade_orders");

            migrationBuilder.DropColumn(
                name: "contract_id",
                table: "trade_orders");
        }
    }
}
