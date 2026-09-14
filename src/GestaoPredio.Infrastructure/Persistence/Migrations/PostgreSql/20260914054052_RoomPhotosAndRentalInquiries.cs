using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class RoomPhotosAndRentalInquiries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrivateFiles_Purpose",
                table: "PrivateFiles");

            migrationBuilder.CreateTable(
                name: "RoomPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsCover = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomPhotos_PrivateFiles_PrivateFileId",
                        column: x => x.PrivateFileId,
                        principalTable: "PrivateFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomPhotos_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoomRentalInquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WhatsApp = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProfessionOrCompany = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PresentedAvailabilityStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PresentedAvailableFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomRentalInquiries", x => x.Id);
                    table.CheckConstraint("CK_RoomRentalInquiries_ConversionState", "(\"Status\" = 'NEW' AND \"LeaseId\" IS NULL AND \"ConvertedAt\" IS NULL) OR (\"Status\" = 'CONVERTED' AND \"LeaseId\" IS NOT NULL AND \"ConvertedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RoomRentalInquiries_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RoomRentalInquiries_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrivateFiles_Purpose",
                table: "PrivateFiles",
                sql: "\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')");

            migrationBuilder.CreateIndex(
                name: "IX_RoomPhotos_PrivateFileId",
                table: "RoomPhotos",
                column: "PrivateFileId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomPhotos_Room_SortOrder",
                table: "RoomPhotos",
                columns: new[] { "RoomId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_RoomPhotos_Room_Cover",
                table: "RoomPhotos",
                column: "RoomId",
                unique: true,
                filter: "\"IsCover\"");

            migrationBuilder.CreateIndex(
                name: "IX_RoomRentalInquiries_LeaseId",
                table: "RoomRentalInquiries",
                column: "LeaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomRentalInquiries_Room_CreatedAt",
                table: "RoomRentalInquiries",
                columns: new[] { "RoomId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomRentalInquiries_Status_CreatedAt",
                table: "RoomRentalInquiries",
                columns: new[] { "Status", "CreatedAt" },
                filter: "\"Status\" = 'NEW'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomPhotos");

            migrationBuilder.DropTable(
                name: "RoomRentalInquiries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PrivateFiles_Purpose",
                table: "PrivateFiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrivateFiles_Purpose",
                table: "PrivateFiles",
                sql: "\"Purpose\" = 'PROFESSIONAL_PHOTO'");
        }
    }
}
