using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase17IdempotencyPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "required_permissions",
                table: "idempotency_records",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "required_permissions",
                table: "idempotency_records");
        }
    }
}
