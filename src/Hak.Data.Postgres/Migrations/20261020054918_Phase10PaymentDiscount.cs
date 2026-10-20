using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase10PaymentDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "payment_discount_days",
                table: "commercial_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payment_discount_percent",
                table: "commercial_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_discount_days",
                table: "business_partners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payment_discount_percent",
                table: "business_partners",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_discount_days",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "payment_discount_percent",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "payment_discount_days",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "payment_discount_percent",
                table: "business_partners");
        }
    }
}
