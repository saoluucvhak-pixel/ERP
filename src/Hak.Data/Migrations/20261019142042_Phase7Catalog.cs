using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "default_vat_rate",
                table: "items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "min_stock",
                table: "items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reorder_qty",
                table: "items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sale_price",
                table: "items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "business_partners",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "credit_limit",
                table: "business_partners",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "business_partners",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_terms_days",
                table: "business_partners",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "business_partners",
                type: "TEXT",
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
