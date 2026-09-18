using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PhoneNumberId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WhatsAppBusinessAccountId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastStatusAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErrorDetails = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppMessages_Direction", "\"Direction\" IN ('OUTBOUND', 'INBOUND')");
                    table.CheckConstraint("CK_WhatsAppMessages_FailureFields", "(\"Status\" = 'FAILED') OR (\"ErrorCode\" IS NULL AND \"ErrorTitle\" IS NULL AND \"ErrorDetails\" IS NULL)");
                    table.CheckConstraint("CK_WhatsAppMessages_Status", "\"Status\" IN ('ACCEPTED', 'SENT', 'DELIVERED', 'READ', 'FAILED')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_Recipient_CreatedAt",
                table: "WhatsAppMessages",
                columns: new[] { "RecipientPhone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_Status_LastStatusAt",
                table: "WhatsAppMessages",
                columns: new[] { "Status", "LastStatusAt" });

            migrationBuilder.CreateIndex(
                name: "UX_WhatsAppMessages_MessageId",
                table: "WhatsAppMessages",
                column: "MessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessages");
        }
    }
}
