using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase11Services : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "inventory_account_id",
                table: "items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "expense_account_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_service",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "revenue_account_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "service_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_acceptances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_acceptance_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_acceptance_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_acceptance_lines_service_acceptances_acceptance_id",
                        column: x => x.acceptance_id,
                        principalTable: "service_acceptances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_expense_account_id",
                table: "items",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_revenue_account_id",
                table: "items",
                column: "revenue_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_acceptance_lines_acceptance_id",
                table: "service_acceptance_lines",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_acceptances_company_id_number",
                table: "service_acceptances",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_acceptances_order_id",
                table: "service_acceptances",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_items_accounts_expense_account_id",
                table: "items",
                column: "expense_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_items_accounts_revenue_account_id",
                table: "items",
                column: "revenue_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_accounts_expense_account_id",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "fk_items_accounts_revenue_account_id",
                table: "items");

            migrationBuilder.DropTable(
                name: "service_acceptance_lines");

            migrationBuilder.DropTable(
                name: "service_acceptances");

            migrationBuilder.DropIndex(
                name: "ix_items_expense_account_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_revenue_account_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "expense_account_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "is_service",
                table: "items");

            migrationBuilder.DropColumn(
                name: "revenue_account_id",
                table: "items");

            migrationBuilder.AlterColumn<Guid>(
                name: "inventory_account_id",
                table: "items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
