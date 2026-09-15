using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddDesiredDatesToRoomRentalInquiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DesiredEndDate",
                table: "RoomRentalInquiries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DesiredStartDate",
                table: "RoomRentalInquiries",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesiredEndDate",
                table: "RoomRentalInquiries");

            migrationBuilder.DropColumn(
                name: "DesiredStartDate",
                table: "RoomRentalInquiries");
        }
    }
}
