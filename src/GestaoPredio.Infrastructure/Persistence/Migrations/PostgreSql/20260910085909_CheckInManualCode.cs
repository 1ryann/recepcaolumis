using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class CheckInManualCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ManualCodeHash",
                table: "CheckInTokens",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CheckInTokens_ManualCodeHash",
                table: "CheckInTokens",
                column: "ManualCodeHash",
                unique: true,
                filter: "\"ManualCodeHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CheckInTokens_ManualCodeHash",
                table: "CheckInTokens");

            migrationBuilder.DropColumn(
                name: "ManualCodeHash",
                table: "CheckInTokens");
        }
    }
}
