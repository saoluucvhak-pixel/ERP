using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase10GroupDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "default_credit_limit",
                table: "catalog_groups",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_payment_terms_days",
                table: "catalog_groups",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_credit_limit",
                table: "catalog_groups");

            migrationBuilder.DropColumn(
                name: "default_payment_terms_days",
                table: "catalog_groups");
        }
    }
}
