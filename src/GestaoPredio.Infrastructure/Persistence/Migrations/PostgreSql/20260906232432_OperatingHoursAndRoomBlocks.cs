using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class OperatingHoursAndRoomBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperatingHoursSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingHoursSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomBlocks", x => x.Id);
                    table.CheckConstraint("CK_RoomBlocks_Period", "\"EndAt\" > \"StartAt\"");
                    table.CheckConstraint("CK_RoomBlocks_Status", "\"Status\" IN ('ACTIVE', 'CANCELLED')");
                    table.ForeignKey(
                        name: "FK_RoomBlocks_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperatingHourIntervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<short>(type: "smallint", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingHourIntervals", x => x.Id);
                    table.CheckConstraint("CK_OperatingHourIntervals_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_OperatingHourIntervals_Period", "\"ClosesAt\" > \"OpensAt\"");
                    table.ForeignKey(
                        name: "FK_OperatingHourIntervals_OperatingHoursSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "OperatingHoursSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_OperatingHourIntervals_Schedule_Day_Open",
                table: "OperatingHourIntervals",
                columns: new[] { "ScheduleId", "DayOfWeek", "OpensAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomBlocks_Room_End",
                table: "RoomBlocks",
                columns: new[] { "RoomId", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomBlocks_Room_Status_Start",
                table: "RoomBlocks",
                columns: new[] { "RoomId", "Status", "StartAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatingHourIntervals");

            migrationBuilder.DropTable(
                name: "RoomBlocks");

            migrationBuilder.DropTable(
                name: "OperatingHoursSchedules");
        }
    }
}
