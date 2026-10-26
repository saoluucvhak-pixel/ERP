using Hak.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <summary>Chỉ thêm trigger bất biến (không đổi mô hình); cùng phạm vi với bản SQLite.</summary>
    [DbContext(typeof(HakDbContext))]
    [Migration("20261026120100_Phase29EvidenceTriggers")]
    public partial class Phase29EvidenceTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.EvidenceStatements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.EvidenceDownStatements()) migrationBuilder.Sql(sql);
        }
    }
}
