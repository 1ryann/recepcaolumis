using GestaoPredio.Domain.Availability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class OperatingHoursScheduleConfiguration : IEntityTypeConfiguration<OperatingHoursSchedule>
{
    public void Configure(EntityTypeBuilder<OperatingHoursSchedule> entity)
    {
        entity.ToTable("OperatingHoursSchedules");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
    }
}
