using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddVisitOpenReservationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Visits_OpenReservation",
                table: "Visits",
                column: "ReservationId",
                unique: true,
                filter: "\"ReservationId\" IS NOT NULL AND \"Status\" IN ('WAITING', 'IN_SERVICE')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Visits_OpenReservation",
                table: "Visits");
        }
    }
}
