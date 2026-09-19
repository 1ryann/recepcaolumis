using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class WhatsAppNotificationUnknownSendOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppNotifications_Lock",
                table: "WhatsAppNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppNotifications_MessageId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppNotifications_Status",
                table: "WhatsAppNotifications");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "WhatsAppNotifications",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppNotifications_Lock",
                table: "WhatsAppNotifications",
                sql: "(\"Status\" IN ('PROCESSING', 'SENDING')) = (\"LockedUntil\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppNotifications_MessageId",
                table: "WhatsAppNotifications",
                sql: "(\"Status\" IN ('ACCEPTED', 'SENT', 'DELIVERED', 'READ') AND \"MessageId\" IS NOT NULL) OR (\"Status\" IN ('PENDING', 'PROCESSING', 'SENDING', 'UNCONFIRMED', 'SKIPPED') AND \"MessageId\" IS NULL) OR \"Status\" = 'FAILED'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppNotifications_Status",
                table: "WhatsAppNotifications",
                sql: "\"Status\" IN ('PENDING', 'PROCESSING', 'ACCEPTED', 'SENT', 'DELIVERED', 'READ', 'FAILED', 'SKIPPED', 'SENDING', 'UNCONFIRMED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppNotifications_Lock",
                table: "WhatsAppNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppNotifications_MessageId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppNotifications_Status",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "WhatsAppNotifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppNotifications_Lock",
                table: "WhatsAppNotifications",
                sql: "(\"Status\" = 'PROCESSING') = (\"LockedUntil\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppNotifications_MessageId",
                table: "WhatsAppNotifications",
                sql: "(\"Status\" IN ('ACCEPTED', 'SENT', 'DELIVERED', 'READ') AND \"MessageId\" IS NOT NULL) OR (\"Status\" IN ('PENDING', 'PROCESSING', 'SKIPPED') AND \"MessageId\" IS NULL) OR \"Status\" = 'FAILED'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppNotifications_Status",
                table: "WhatsAppNotifications",
                sql: "\"Status\" IN ('PENDING', 'PROCESSING', 'ACCEPTED', 'SENT', 'DELIVERED', 'READ', 'FAILED', 'SKIPPED')");
        }
    }
}
