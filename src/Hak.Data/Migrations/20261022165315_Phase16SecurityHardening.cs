using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase16SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_chain_anchors",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    legacy_seq = table.Column<long>(type: "INTEGER", nullable: false),
                    legacy_hash = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    mac = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_chain_anchors", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "login_throttles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    failed_count = table.Column<int>(type: "INTEGER", nullable: false),
                    locked_until = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_throttles", x => new { x.user_id, x.source });
                    table.ForeignKey(
                        name: "fk_login_throttles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
            // Mốc chuyển chuỗi nhật ký sang có khóa (SEC-09): chỉ thêm, không sửa / xóa.
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_chain_anchors_no_update BEFORE UPDATE ON audit_chain_anchors BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_chain_anchors_no_delete BEFORE DELETE ON audit_chain_anchors BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_chain_anchors_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_chain_anchors_no_delete;");
            migrationBuilder.DropTable(
                name: "audit_chain_anchors");

            migrationBuilder.DropTable(
                name: "login_throttles");
        }
    }
}
