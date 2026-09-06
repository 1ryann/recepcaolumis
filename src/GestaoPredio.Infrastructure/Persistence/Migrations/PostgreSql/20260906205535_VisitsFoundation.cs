using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class VisitsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ArrivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServiceStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                    table.CheckConstraint("CK_Visits_StateTimestamps", "(\"Status\" = 'WAITING' AND \"ServiceStartedAt\" IS NULL AND \"EndedAt\" IS NULL AND \"CancelledAt\" IS NULL)\nOR (\"Status\" = 'IN_SERVICE' AND \"ServiceStartedAt\" IS NOT NULL AND \"EndedAt\" IS NULL AND \"CancelledAt\" IS NULL)\nOR (\"Status\" = 'ENDED' AND \"ServiceStartedAt\" IS NOT NULL AND \"EndedAt\" IS NOT NULL AND \"CancelledAt\" IS NULL)\nOR (\"Status\" = 'CANCELLED' AND \"EndedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Visits_Status", "\"Status\" IN ('WAITING', 'IN_SERVICE', 'ENDED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "FK_Visits_Professionals_ProfessionalId",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Visits_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Visits_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VisitTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCorrection = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitTransitions", x => x.Id);
                    table.CheckConstraint("CK_VisitTransitions_CorrectionReason", "NOT \"IsCorrection\" OR \"Reason\" IS NOT NULL");
                    table.CheckConstraint("CK_VisitTransitions_NewStatus", "\"NewStatus\" IN ('WAITING', 'IN_SERVICE', 'ENDED', 'CANCELLED')");
                    table.CheckConstraint("CK_VisitTransitions_PreviousStatus", "\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN ('WAITING', 'IN_SERVICE', 'ENDED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "FK_VisitTransitions_Visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "Visits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_Professional_Status_ArrivedAt",
                table: "Visits",
                columns: new[] { "ProfessionalId", "Status", "ArrivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_ReservationId",
                table: "Visits",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_Room_Status_ArrivedAt",
                table: "Visits",
                columns: new[] { "RoomId", "Status", "ArrivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_Status_ArrivedAt",
                table: "Visits",
                columns: new[] { "Status", "ArrivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitTransitions_Visit_OccurredAt",
                table: "VisitTransitions",
                columns: new[] { "VisitId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisitTransitions");

            migrationBuilder.DropTable(
                name: "Visits");
        }
    }
}
