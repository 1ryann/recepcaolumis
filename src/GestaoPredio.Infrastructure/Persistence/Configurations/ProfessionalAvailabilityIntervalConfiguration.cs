using GestaoPredio.Domain.Professionals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalAvailabilityIntervalConfiguration
    : IEntityTypeConfiguration<ProfessionalAvailabilityInterval>
{
    public void Configure(EntityTypeBuilder<ProfessionalAvailabilityInterval> entity)
    {
        entity.ToTable("ProfessionalAvailabilityIntervals", table =>
        {
            table.HasCheckConstraint("CK_ProfessionalAvailabilityIntervals_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_ProfessionalAvailabilityIntervals_Period", "\"EndTime\" > \"StartTime\"");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.DayOfWeek).HasColumnType("smallint").HasConversion<short>().IsRequired();
        entity.Property(x => x.StartTime).HasColumnType("time without time zone").IsRequired();
        entity.Property(x => x.EndTime).HasColumnType("time without time zone").IsRequired();
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.ProfessionalId, x.DayOfWeek, x.StartTime }).IsUnique()
            .HasDatabaseName("UX_ProfessionalAvailabilityIntervals_Professional_Day_Start");
    }
}
