using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10Groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    domain = table.Column<int>(type: "INTEGER", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    parent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_catalog_groups_catalog_groups_parent_id",
                        column: x => x.parent_id,
                        principalTable: "catalog_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalog_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    group_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    entity_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_catalog_group_members_catalog_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "catalog_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_group_members_company_id_entity_id",
                table: "catalog_group_members",
                columns: new[] { "company_id", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_group_members_group_id_entity_id",
                table: "catalog_group_members",
                columns: new[] { "group_id", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_groups_company_id_domain_code",
                table: "catalog_groups",
                columns: new[] { "company_id", "domain", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_groups_parent_id",
                table: "catalog_groups",
                column: "parent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_group_members");

            migrationBuilder.DropTable(
                name: "catalog_groups");
        }
    }
}
