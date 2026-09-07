using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestaoPredio.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class FinancialChargesFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferencePeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReferencePeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CalculatedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CalculationDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AdjustmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialCharges", x => x.Id);
                    table.CheckConstraint("CK_FinancialCharges_Amounts", "\"CalculatedAmount\" >= 0 AND \"FinalAmount\" >= 0");
                    table.CheckConstraint("CK_FinancialCharges_Period", "\"ReferencePeriodEnd\" > \"ReferencePeriodStart\"");
                    table.CheckConstraint("CK_FinancialCharges_Status", "\"Status\" IN ('PENDING', 'PAID', 'CANCELLED')");
                    table.ForeignKey(
                        name: "FK_FinancialCharges_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialCharges_Professionals_ProfessionalId",
                        column: x => x.ProfessionalId,
                        principalTable: "Professionals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialCharges_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_LeaseId",
                table: "FinancialCharges",
                column: "LeaseId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_Period",
                table: "FinancialCharges",
                columns: new[] { "ReferencePeriodStart", "ReferencePeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_ProfessionalId",
                table: "FinancialCharges",
                column: "ProfessionalId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_Status_DueDate",
                table: "FinancialCharges",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_TenantId",
                table: "FinancialCharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_FinancialCharges_Lease_Period",
                table: "FinancialCharges",
                columns: new[] { "LeaseId", "ReferencePeriodStart", "ReferencePeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialCharges");
        }
    }
}
