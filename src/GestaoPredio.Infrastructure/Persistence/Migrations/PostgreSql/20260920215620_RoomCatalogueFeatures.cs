using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class RoomCatalogueFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Amenities",
                table: "Rooms",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<decimal>(
                name: "AreaSquareMeters",
                table: "Rooms",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "BathroomCount",
                table: "Rooms",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "CapacityMax",
                table: "Rooms",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "CapacityMin",
                table: "Rooms",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Rooms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyRate",
                table: "Rooms",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rooms_Area_Positive",
                table: "Rooms",
                sql: "\"AreaSquareMeters\" IS NULL OR (\"AreaSquareMeters\" > 0 AND \"AreaSquareMeters\" < 100000)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rooms_BathroomCount",
                table: "Rooms",
                sql: "\"BathroomCount\" IS NULL OR \"BathroomCount\" BETWEEN 0 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rooms_Capacity",
                table: "Rooms",
                sql: "(\"CapacityMin\" IS NULL AND \"CapacityMax\" IS NULL) OR (\"CapacityMin\" BETWEEN 1 AND 200 AND (\"CapacityMax\" IS NULL OR (\"CapacityMax\" >= \"CapacityMin\" AND \"CapacityMax\" <= 200)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rooms_Category",
                table: "Rooms",
                sql: "\"Category\" IS NULL OR \"Category\" IN ('CONSULTORIO', 'REUNIAO', 'CRIATIVA')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rooms_MonthlyRate_NonNegative",
                table: "Rooms",
                sql: "\"MonthlyRate\" IS NULL OR \"MonthlyRate\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Rooms_Area_Positive",
                table: "Rooms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rooms_BathroomCount",
                table: "Rooms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rooms_Capacity",
                table: "Rooms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rooms_Category",
                table: "Rooms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rooms_MonthlyRate_NonNegative",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Amenities",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "AreaSquareMeters",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "BathroomCount",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "CapacityMax",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "CapacityMin",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "MonthlyRate",
                table: "Rooms");
        }
    }
}
