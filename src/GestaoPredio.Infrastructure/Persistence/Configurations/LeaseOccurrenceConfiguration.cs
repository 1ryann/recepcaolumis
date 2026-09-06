using GestaoPredio.Domain.Leases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class LeaseOccurrenceConfiguration : IEntityTypeConfiguration<LeaseOccurrence>
{
    public void Configure(EntityTypeBuilder<LeaseOccurrence> entity)
    {
        entity.ToTable("LeaseOccurrences", table =>
            table.HasCheckConstraint("CK_LeaseOccurrences_State", "\"State\" IN ('PLANNED', 'CANCELLED', 'COMPLETED')"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StartAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.EndAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.State)
            .HasConversion(new ValueConverter<LeaseOccurrenceState, string>(
                value => value == LeaseOccurrenceState.Planned ? "PLANNED" : value == LeaseOccurrenceState.Cancelled ? "CANCELLED" : "COMPLETED",
                value => value == "PLANNED" ? LeaseOccurrenceState.Planned : value == "CANCELLED" ? LeaseOccurrenceState.Cancelled : LeaseOccurrenceState.Completed))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.LeaseId, x.StartAt }).IsUnique()
            .HasDatabaseName("UX_LeaseOccurrences_LeaseId_StartAt");
    }
}
