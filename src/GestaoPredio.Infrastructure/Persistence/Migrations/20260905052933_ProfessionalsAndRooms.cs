using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalsAndRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangedFields",
                table: "AuditEntries",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetEntityId",
                table: "AuditEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetEntityType",
                table: "AuditEntries",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrivateFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateFiles", x => x.Id);
                    table.CheckConstraint("CK_PrivateFiles_Length_Positive", "[Length] > 0");
                    table.CheckConstraint("CK_PrivateFiles_Purpose", "[Purpose] = 'PROFESSIONAL_PHOTO'");
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HourlyRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                    table.CheckConstraint("CK_Rooms_DailyRate_NonNegative", "[DailyRate] >= 0");
                    table.CheckConstraint("CK_Rooms_HourlyRate_NonNegative", "[HourlyRate] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Professionals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Profession = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedProfession = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    WhatsApp = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    PhotoFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Professionals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Professionals_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Professionals_PrivateFiles_PhotoFileId",
                        column: x => x.PhotoFileId,
                        principalTable: "PrivateFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TargetEntity",
                table: "AuditEntries",
                columns: new[] { "TargetEntityType", "TargetEntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PrivateFiles_StorageKey",
                table: "PrivateFiles",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Professionals_ApplicationUserId",
                table: "Professionals",
                column: "ApplicationUserId",
                unique: true,
                filter: "[ApplicationUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Professionals_PhotoFileId",
                table: "Professionals",
                column: "PhotoFileId",
                unique: true,
                filter: "[PhotoFileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Rooms_NormalizedName",
                table: "Rooms",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Professionals");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "PrivateFiles");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TargetEntity",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "ChangedFields",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "TargetEntityId",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "TargetEntityType",
                table: "AuditEntries");
        }
    }
}
