using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase14Idempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    method = table.Column<string>(type: "TEXT", nullable: false),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    status_code = table.Column<int>(type: "INTEGER", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", nullable: true),
                    response_body = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_created_at",
                table: "idempotency_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_scope_key",
                table: "idempotency_records",
                columns: new[] { "scope", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
