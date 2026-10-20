using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "default_vat_rate",
                table: "items",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_stock",
                table: "items",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reorder_qty",
                table: "items",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "sale_price",
                table: "items",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "business_partners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit",
                table: "business_partners",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "business_partners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_terms_days",
                table: "business_partners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "business_partners",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_vat_rate",
                table: "items");

            migrationBuilder.DropColumn(
                name: "min_stock",
                table: "items");

            migrationBuilder.DropColumn(
                name: "reorder_qty",
                table: "items");

            migrationBuilder.DropColumn(
                name: "sale_price",
                table: "items");

            migrationBuilder.DropColumn(
                name: "address",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "email",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "payment_terms_days",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "business_partners");
        }
    }
}
