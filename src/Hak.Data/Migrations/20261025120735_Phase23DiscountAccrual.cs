using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase23DiscountAccrual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "accrual_reversal_voucher_id",
                table: "discount_agreement_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "accrued_amount",
                table: "discount_agreement_runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "discount_accruals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    agreement_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    as_of = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    estimated = table.Column<long>(type: "INTEGER", nullable: false),
                    adjustment = table.Column<long>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_accruals", x => x.id);
                    table.ForeignKey(
                        name: "fk_discount_accruals_discount_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalTable: "discount_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discount_accruals_agreement_id_as_of",
                table: "discount_accruals",
                columns: new[] { "agreement_id", "as_of" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discount_accruals");

            migrationBuilder.DropColumn(
                name: "accrual_reversal_voucher_id",
                table: "discount_agreement_runs");

            migrationBuilder.DropColumn(
                name: "accrued_amount",
                table: "discount_agreement_runs");
        }
    }
}
