using GestaoPredio.Domain.Availability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class OperatingHourIntervalConfiguration : IEntityTypeConfiguration<OperatingHourInterval>
{
    public void Configure(EntityTypeBuilder<OperatingHourInterval> entity)
    {
        entity.ToTable("OperatingHourIntervals", table =>
        {
            table.HasCheckConstraint("CK_OperatingHourIntervals_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_OperatingHourIntervals_Period", "\"ClosesAt\" > \"OpensAt\"");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.DayOfWeek).HasColumnType("smallint").IsRequired();
        entity.Property(x => x.OpensAt).HasColumnType("time without time zone");
        entity.Property(x => x.ClosesAt).HasColumnType("time without time zone");
        entity.HasOne<OperatingHoursSchedule>().WithMany().HasForeignKey(x => x.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(x => new { x.ScheduleId, x.DayOfWeek, x.OpensAt }).IsUnique()
            .HasDatabaseName("UX_OperatingHourIntervals_Schedule_Day_Open");
    }
}
