using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class ProfessionalAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "AvailabilityMode",
                table: "Professionals",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "ProfessionalAvailabilityExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AllDay = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfessionalAvailabilityExceptions", x => x.Id);
                    table.CheckConstraint("CK_ProfessionalAvailabilityExceptions_Shape", "(\"AllDay\" = TRUE AND \"StartTime\" IS NULL AND \"EndTime\" IS NULL) OR (\"AllDay\" = FALSE AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"EndTime\" > \"StartTime\")");
                    table.ForeignKey(
                        name: "FK_ProfessionalAvailabilityExceptions_Professionals_Profession~",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProfessionalAvailabilityIntervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<short>(type: "smallint", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfessionalAvailabilityIntervals", x => x.Id);
                    table.CheckConstraint("CK_ProfessionalAvailabilityIntervals_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_ProfessionalAvailabilityIntervals_Period", "\"EndTime\" > \"StartTime\"");
                    table.ForeignKey(
                        name: "FK_ProfessionalAvailabilityIntervals_Professionals_Professiona~",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Professionals_AvailabilityMode",
                table: "Professionals",
                sql: "\"AvailabilityMode\" BETWEEN 0 AND 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProfessionalAvailabilityExceptions_Professional_Date_Start",
                table: "ProfessionalAvailabilityExceptions",
                columns: new[] { "ProfessionalId", "Date", "AllDay", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "UX_ProfessionalAvailabilityExceptions_Professional_Date_AllDay",
                table: "ProfessionalAvailabilityExceptions",
                columns: new[] { "ProfessionalId", "Date" },
                unique: true,
                filter: "\"AllDay\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_ProfessionalAvailabilityExceptions_Professional_Date_Start",
                table: "ProfessionalAvailabilityExceptions",
                columns: new[] { "ProfessionalId", "Date", "StartTime" },
                unique: true,
                filter: "\"AllDay\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_ProfessionalAvailabilityIntervals_Professional_Day_Start",
                table: "ProfessionalAvailabilityIntervals",
                columns: new[] { "ProfessionalId", "DayOfWeek", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfessionalAvailabilityExceptions");

            migrationBuilder.DropTable(
                name: "ProfessionalAvailabilityIntervals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Professionals_AvailabilityMode",
                table: "Professionals");

            migrationBuilder.DropColumn(
                name: "AvailabilityMode",
                table: "Professionals");
        }
    }
}
