using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase24PaymentDiscountOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_discount_options",
                table: "commercial_invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_discount_options",
                table: "business_partners",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_discount_options",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "payment_discount_options",
                table: "business_partners");
        }
    }
}
