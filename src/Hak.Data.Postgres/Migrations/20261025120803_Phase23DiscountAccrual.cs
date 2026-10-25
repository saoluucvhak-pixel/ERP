using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "accrued_amount",
                table: "discount_agreement_runs",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "discount_accruals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    estimated = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    adjustment = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
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
