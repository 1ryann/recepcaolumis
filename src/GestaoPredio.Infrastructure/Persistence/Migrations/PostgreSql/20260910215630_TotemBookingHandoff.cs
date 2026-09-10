using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class TotemBookingHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TotemBookingHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    HandoffTokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    StatusTokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotemBookingHandoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TotemBookingHandoffs_Professionals_ProfessionalId",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TotemBookingHandoffs_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TotemBookingHandoffs_ProfessionalId",
                table: "TotemBookingHandoffs",
                column: "ProfessionalId");

            migrationBuilder.CreateIndex(
                name: "IX_TotemBookingHandoffs_ReservationId",
                table: "TotemBookingHandoffs",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_TotemBookingHandoffs_Status_ExpiresAt",
                table: "TotemBookingHandoffs",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_TotemBookingHandoffs_HandoffTokenHash",
                table: "TotemBookingHandoffs",
                column: "HandoffTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TotemBookingHandoffs_StatusTokenHash",
                table: "TotemBookingHandoffs",
                column: "StatusTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TotemBookingHandoffs");
        }
    }
}
