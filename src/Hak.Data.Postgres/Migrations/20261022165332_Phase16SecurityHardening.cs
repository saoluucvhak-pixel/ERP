using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legacy_seq = table.Column<long>(type: "bigint", nullable: false),
                    legacy_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    mac = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_chain_anchors", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "login_throttles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_chain_anchors_append_only BEFORE UPDATE OR DELETE ON audit_chain_anchors FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_chain_anchors_append_only ON audit_chain_anchors;");
            migrationBuilder.DropTable(
                name: "audit_chain_anchors");

            migrationBuilder.DropTable(
                name: "login_throttles");
        }
    }
}
