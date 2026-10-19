using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase8LoanSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loan_schedule_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    loan_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    principal_due = table.Column<long>(type: "INTEGER", nullable: false),
                    interest_due = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_schedule_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_schedule_items_loan_contracts_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loan_contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_schedule_items_loan_id_due_date",
                table: "loan_schedule_items",
                columns: new[] { "loan_id", "due_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_schedule_items");
        }
    }
}
