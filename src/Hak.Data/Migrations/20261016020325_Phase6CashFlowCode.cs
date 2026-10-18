using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6CashFlowCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cash_flow_code",
                table: "voucher_lines",
                type: "TEXT",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cash_flow_code",
                table: "journal_lines",
                type: "TEXT",
                maxLength: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cash_flow_code",
                table: "voucher_lines");

            migrationBuilder.DropColumn(
                name: "cash_flow_code",
                table: "journal_lines");
        }
    }
}
