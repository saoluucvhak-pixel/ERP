using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10GroupAssetDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_accumulated_account_id",
                table: "catalog_groups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_asset_account_id",
                table: "catalog_groups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_depreciation_method",
                table: "catalog_groups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_expense_account_id",
                table: "catalog_groups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_useful_life_months",
                table: "catalog_groups",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_accumulated_account_id",
                table: "catalog_groups");

            migrationBuilder.DropColumn(
                name: "default_asset_account_id",
                table: "catalog_groups");

            migrationBuilder.DropColumn(
                name: "default_depreciation_method",
                table: "catalog_groups");

            migrationBuilder.DropColumn(
                name: "default_expense_account_id",
                table: "catalog_groups");

            migrationBuilder.DropColumn(
                name: "default_useful_life_months",
                table: "catalog_groups");
        }
    }
}
