using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddWhatsAppNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppNotifications", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppNotifications_Attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_WhatsAppNotifications_Lock", "(\"Status\" = 'PROCESSING') = (\"LockedUntil\" IS NOT NULL)");
                    table.CheckConstraint("CK_WhatsAppNotifications_MessageId", "(\"Status\" IN ('ACCEPTED', 'SENT', 'DELIVERED', 'READ') AND \"MessageId\" IS NOT NULL) OR (\"Status\" IN ('PENDING', 'PROCESSING', 'SKIPPED') AND \"MessageId\" IS NULL) OR \"Status\" = 'FAILED'");
                    table.CheckConstraint("CK_WhatsAppNotifications_Recipient", "\"Recipient\" IN ('PROFESSIONAL', 'CUSTOMER')");
                    table.CheckConstraint("CK_WhatsAppNotifications_Status", "\"Status\" IN ('PENDING', 'PROCESSING', 'ACCEPTED', 'SENT', 'DELIVERED', 'READ', 'FAILED', 'SKIPPED')");
                    table.CheckConstraint("CK_WhatsAppNotifications_Type", "\"Type\" IN ('CLIENT_CHECKED_IN', 'PROFESSIONAL_CANCELLED', 'PROFESSIONAL_DELAYED', 'APPOINTMENT_RESCHEDULED', 'APPOINTMENT_CONFIRMED', 'APPOINTMENT_CANCELLED', 'APPOINTMENT_REMINDER')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_MessageId",
                table: "WhatsAppNotifications",
                column: "MessageId",
                filter: "\"MessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_ReservationId",
                table: "WhatsAppNotifications",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_Status_NextAttemptAt",
                table: "WhatsAppNotifications",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "UX_WhatsAppNotifications_IdempotencyKey",
                table: "WhatsAppNotifications",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppNotifications");
        }
    }
}
