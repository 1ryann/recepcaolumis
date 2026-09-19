using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class WhatsAppOptIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WhatsAppOptInChangedAt",
                table: "Professionals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInSource",
                table: "Professionals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInStatus",
                table: "Professionals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NOT_RECORDED");   // existing rows: no opt-in is ever assumed; also keeps older code valid

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInTextVersion",
                table: "Professionals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WhatsAppOptInChangedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInSource",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInStatus",
                table: "Customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NOT_RECORDED");   // existing rows: no opt-in is ever assumed; also keeps older code valid

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInTextVersion",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Professionals_WhatsAppOptInDecision",
                table: "Professionals",
                sql: "\"WhatsAppOptInStatus\" = 'NOT_RECORDED' OR (\"WhatsAppOptInChangedAt\" IS NOT NULL AND \"WhatsAppOptInSource\" IS NOT NULL AND (\"WhatsAppOptInStatus\" <> 'GRANTED' OR \"WhatsAppOptInTextVersion\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Professionals_WhatsAppOptInSource",
                table: "Professionals",
                sql: "\"WhatsAppOptInSource\" IS NULL OR \"WhatsAppOptInSource\" IN ('CUSTOMER_REGISTRATION', 'CUSTOMER_PORTAL', 'TOTEM', 'RECEPTION', 'PROFESSIONAL_PORTAL')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Professionals_WhatsAppOptInStatus",
                table: "Professionals",
                sql: "\"WhatsAppOptInStatus\" IN ('NOT_RECORDED', 'GRANTED', 'REVOKED')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_WhatsAppOptInDecision",
                table: "Customers",
                sql: "\"WhatsAppOptInStatus\" = 'NOT_RECORDED' OR (\"WhatsAppOptInChangedAt\" IS NOT NULL AND \"WhatsAppOptInSource\" IS NOT NULL AND (\"WhatsAppOptInStatus\" <> 'GRANTED' OR \"WhatsAppOptInTextVersion\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_WhatsAppOptInSource",
                table: "Customers",
                sql: "\"WhatsAppOptInSource\" IS NULL OR \"WhatsAppOptInSource\" IN ('CUSTOMER_REGISTRATION', 'CUSTOMER_PORTAL', 'TOTEM', 'RECEPTION', 'PROFESSIONAL_PORTAL')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_WhatsAppOptInStatus",
                table: "Customers",
                sql: "\"WhatsAppOptInStatus\" IN ('NOT_RECORDED', 'GRANTED', 'REVOKED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Professionals_WhatsAppOptInDecision",
                table: "Professionals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Professionals_WhatsAppOptInSource",
                table: "Professionals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Professionals_WhatsAppOptInStatus",
                table: "Professionals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_WhatsAppOptInDecision",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_WhatsAppOptInSource",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_WhatsAppOptInStatus",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInChangedAt",
                table: "Professionals");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInSource",
                table: "Professionals");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInStatus",
                table: "Professionals");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInTextVersion",
                table: "Professionals");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInChangedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInSource",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInStatus",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInTextVersion",
                table: "Customers");
        }
    }
}
