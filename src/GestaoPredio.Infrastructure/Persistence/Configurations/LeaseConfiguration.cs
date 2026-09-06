using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> entity)
    {
        entity.ToTable("Leases", table =>
        {
            table.HasCheckConstraint("CK_Leases_Mode", "\"Mode\" IN ('MONTHLY', 'DAILY', 'HOURLY')");
            table.HasCheckConstraint("CK_Leases_LifecycleState", "\"LifecycleState\" IN ('OPEN', 'ENDING_PENDING', 'ENDED', 'CANCELLED')");
            table.HasCheckConstraint("CK_Leases_ContractedRate", "\"ContractedRate\" >= 0");
            table.HasCheckConstraint("CK_Leases_BillingDueDay", "\"BillingDueDay\" IS NULL OR \"BillingDueDay\" BETWEEN 1 AND 31");
            table.HasCheckConstraint("CK_Leases_DatesAndMode", "\"BillingStartAt\" <= \"OccupancyStartAt\" AND (\"OccupancyEndAt\" IS NULL OR \"OccupancyEndAt\" > \"OccupancyStartAt\") AND ((\"Mode\" = 'MONTHLY' AND \"MonthlyAnchorDay\" BETWEEN 1 AND 31) OR (\"Mode\" IN ('DAILY', 'HOURLY') AND \"OccupancyEndAt\" IS NOT NULL AND \"MonthlyAnchorDay\" IS NULL))");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Mode)
            .HasConversion(new ValueConverter<LeaseMode, string>(
                value => value == LeaseMode.Monthly ? "MONTHLY" : value == LeaseMode.Daily ? "DAILY" : "HOURLY",
                value => value == "MONTHLY" ? LeaseMode.Monthly : value == "DAILY" ? LeaseMode.Daily : LeaseMode.Hourly))
            .HasMaxLength(10).IsRequired();
        entity.Property(x => x.LifecycleState)
            .HasConversion(new ValueConverter<LeaseLifecycleState, string>(
                value => value == LeaseLifecycleState.Open ? "OPEN" : value == LeaseLifecycleState.EndingPending ? "ENDING_PENDING" : value == LeaseLifecycleState.Ended ? "ENDED" : "CANCELLED",
                value => value == "OPEN" ? LeaseLifecycleState.Open : value == "ENDING_PENDING" ? LeaseLifecycleState.EndingPending : value == "ENDED" ? LeaseLifecycleState.Ended : LeaseLifecycleState.Cancelled))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.ContractedRate).HasPrecision(18, 2);
        entity.Property(x => x.BillingStartAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.BillingDueDay).HasColumnType("smallint");
        entity.Property(x => x.OccupancyStartAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.OccupancyEndAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.MonthlyAnchorDay).HasColumnType("smallint");
        entity.Property(x => x.MaterializedThroughAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.RoomId, x.LifecycleState, x.OccupancyStartAt }).HasDatabaseName("IX_Leases_Room_State_Start");
        entity.HasIndex(x => new { x.ProfessionalId, x.LifecycleState, x.OccupancyStartAt }).HasDatabaseName("IX_Leases_Professional_State_Start");
        entity.HasIndex(x => new { x.TenantId, x.LifecycleState, x.BillingStartAt }).HasDatabaseName("IX_Leases_Tenant_State_Billing");
    }
}
