using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <summary>DB-08: các hàm trigger tài sản / khấu hao khóa thêm cột cho khớp interceptor và bản SQLite (xem ImmutabilityTriggers.ParityStatements).</summary>
    public partial class Phase15ImmutabilityParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.ParityStatements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.ParityDownStatements()) migrationBuilder.Sql(sql);
        }
    }
}
