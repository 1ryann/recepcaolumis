using GestaoPredio.Domain.Finance;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class FinancialChargeConfiguration : IEntityTypeConfiguration<FinancialCharge>
{
    public void Configure(EntityTypeBuilder<FinancialCharge> entity)
    {
        entity.ToTable("FinancialCharges", table =>
        {
            table.HasCheckConstraint("CK_FinancialCharges_Status", "\"Status\" IN ('PENDING', 'PAID', 'CANCELLED')");
            table.HasCheckConstraint("CK_FinancialCharges_Amounts", "\"CalculatedAmount\" >= 0 AND \"FinalAmount\" >= 0");
            table.HasCheckConstraint("CK_FinancialCharges_Period", "\"ReferencePeriodEnd\" > \"ReferencePeriodStart\"");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Status)
            .HasConversion(new ValueConverter<FinancialChargeStatus, string>(
                value => value == FinancialChargeStatus.Pending ? "PENDING" : value == FinancialChargeStatus.Paid ? "PAID" : "CANCELLED",
                value => value == "PENDING" ? FinancialChargeStatus.Pending : value == "PAID" ? FinancialChargeStatus.Paid : FinancialChargeStatus.Cancelled))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.CalculatedAmount).HasPrecision(18, 2).IsRequired();
        entity.Property(x => x.FinalAmount).HasPrecision(18, 2).IsRequired();
        entity.Property(x => x.CalculationDetails).HasMaxLength(FinancialCharge.MaximumDetailsLength).IsRequired();
        entity.Property(x => x.AdjustmentReason).HasMaxLength(FinancialCharge.MaximumReasonLength);
        entity.Property(x => x.CancellationReason).HasMaxLength(FinancialCharge.MaximumReasonLength);
        entity.Property(x => x.ReferencePeriodStart).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ReferencePeriodEnd).HasColumnType("timestamp with time zone");
        entity.Property(x => x.DueDate).HasColumnType("date");
        entity.Property(x => x.PaidAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.LeaseId, x.ReferencePeriodStart, x.ReferencePeriodEnd })
            .IsUnique().HasDatabaseName("UX_FinancialCharges_Lease_Period");
        entity.HasIndex(x => new { x.Status, x.DueDate }).HasDatabaseName("IX_FinancialCharges_Status_DueDate");
        entity.HasIndex(x => x.LeaseId).HasDatabaseName("IX_FinancialCharges_LeaseId");
        entity.HasIndex(x => x.TenantId).HasDatabaseName("IX_FinancialCharges_TenantId");
        entity.HasIndex(x => x.ProfessionalId).HasDatabaseName("IX_FinancialCharges_ProfessionalId");
        entity.HasIndex(x => new { x.ReferencePeriodStart, x.ReferencePeriodEnd }).HasDatabaseName("IX_FinancialCharges_Period");
    }
}
