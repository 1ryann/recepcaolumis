using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class ReservationsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_Kind", "\"Kind\" IN ('NEW', 'RESCHEDULE', 'CANCELLATION')");
                    table.CheckConstraint("CK_Reservations_Period", "\"EndAt\" > \"StartAt\"");
                    table.CheckConstraint("CK_Reservations_RejectionReason", "\"Status\" <> 'REJECTED' OR \"RejectionReason\" IS NOT NULL");
                    table.CheckConstraint("CK_Reservations_Status", "\"Status\" IN ('PENDING', 'APPROVED', 'REJECTED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "FK_Reservations_Professionals_ProfessionalId",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reservations_Reservations_OriginalReservationId",
                        column: x => x.OriginalReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reservations_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_OriginalReservationId",
                table: "Reservations",
                column: "OriginalReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Professional_Status_Start",
                table: "Reservations",
                columns: new[] { "ProfessionalId", "Status", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Room_Status_Start",
                table: "Reservations",
                columns: new[] { "RoomId", "Status", "StartAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
