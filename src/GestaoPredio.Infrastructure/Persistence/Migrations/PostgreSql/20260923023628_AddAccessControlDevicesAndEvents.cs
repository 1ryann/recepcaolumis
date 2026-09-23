using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddAccessControlDevicesAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Disposition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadShape = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessEvents", x => x.Id);
                    table.CheckConstraint("CK_AccessEvents_Disposition", "\"Disposition\" IN ('CAPTURED', 'DUPLICATE', 'REJECTED')");
                    table.ForeignKey(
                        name: "FK_AccessEvents_AccessDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "AccessDevices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_AccessDevices_SecretHash",
                table: "AccessDevices",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccessDevices_SerialNumber",
                table: "AccessDevices",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessEvents_Device_ReceivedAt",
                table: "AccessEvents",
                columns: new[] { "DeviceId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AccessEvents_IdempotencyKey",
                table: "AccessEvents",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessEvents");

            migrationBuilder.DropTable(
                name: "AccessDevices");
        }
    }
}
