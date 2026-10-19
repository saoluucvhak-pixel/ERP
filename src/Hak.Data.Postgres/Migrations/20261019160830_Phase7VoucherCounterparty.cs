using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase7VoucherCounterparty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "counterparty_name",
                table: "vouchers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "counterparty_unit",
                table: "vouchers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "counterparty_name",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "counterparty_unit",
                table: "vouchers");
        }
    }
}
