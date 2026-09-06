using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class LeasesFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.CheckConstraint("CK_Tenants_Kind", "\"Kind\" IN ('INDIVIDUAL', 'LEGAL_ENTITY')");
                });

            migrationBuilder.CreateTable(
                name: "Leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ContractedRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BillingStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BillingDueDay = table.Column<short>(type: "smallint", nullable: true),
                    OccupancyStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccupancyEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LifecycleState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MonthlyAnchorDay = table.Column<short>(type: "smallint", nullable: true),
                    MaterializedThroughAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => x.Id);
                    table.CheckConstraint("CK_Leases_BillingDueDay", "\"BillingDueDay\" IS NULL OR \"BillingDueDay\" BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_Leases_ContractedRate", "\"ContractedRate\" >= 0");
                    table.CheckConstraint("CK_Leases_DatesAndMode", "\"BillingStartAt\" <= \"OccupancyStartAt\" AND (\"OccupancyEndAt\" IS NULL OR \"OccupancyEndAt\" > \"OccupancyStartAt\") AND ((\"Mode\" = 'MONTHLY' AND \"MonthlyAnchorDay\" BETWEEN 1 AND 31) OR (\"Mode\" IN ('DAILY', 'HOURLY') AND \"OccupancyEndAt\" IS NOT NULL AND \"MonthlyAnchorDay\" IS NULL))");
                    table.CheckConstraint("CK_Leases_LifecycleState", "\"LifecycleState\" IN ('OPEN', 'ENDING_PENDING', 'ENDED', 'CANCELLED')");
                    table.CheckConstraint("CK_Leases_Mode", "\"Mode\" IN ('MONTHLY', 'DAILY', 'HOURLY')");
                    table.ForeignKey(
                        name: "FK_Leases_Professionals_ProfessionalId",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leases_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leases_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaseOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseOccurrences", x => x.Id);
                    table.CheckConstraint("CK_LeaseOccurrences_State", "\"State\" IN ('PLANNED', 'CANCELLED', 'COMPLETED')");
                    table.ForeignKey(
                        name: "FK_LeaseOccurrences_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_LeaseOccurrences_LeaseId_StartAt",
                table: "LeaseOccurrences",
                columns: new[] { "LeaseId", "StartAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leases_Professional_State_Start",
                table: "Leases",
                columns: new[] { "ProfessionalId", "LifecycleState", "OccupancyStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_Room_State_Start",
                table: "Leases",
                columns: new[] { "RoomId", "LifecycleState", "OccupancyStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_Tenant_State_Billing",
                table: "Leases",
                columns: new[] { "TenantId", "LifecycleState", "BillingStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_NormalizedName",
                table: "Tenants",
                columns: new[] { "NormalizedName", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaseOccurrences");

            migrationBuilder.DropTable(
                name: "Leases");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
