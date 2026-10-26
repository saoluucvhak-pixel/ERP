using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase29PurchaseRebatePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "purchase_rebate_policy",
                table: "companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite không hỗ trợ DropColumnOperation của EF; dùng DROP COLUMN gốc (SQLite >= 3.35) như Phase27.
            migrationBuilder.Sql("ALTER TABLE companies DROP COLUMN purchase_rebate_policy;");
        }
    }
}
