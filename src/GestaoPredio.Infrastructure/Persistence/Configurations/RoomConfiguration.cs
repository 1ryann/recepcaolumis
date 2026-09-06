using GestaoPredio.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> entity)
    {
        entity.ToTable("Rooms", table =>
        {
            table.HasCheckConstraint("CK_Rooms_HourlyRate_NonNegative", "\"HourlyRate\" >= 0");
            table.HasCheckConstraint("CK_Rooms_DailyRate_NonNegative", "\"DailyRate\" >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.HourlyRate).HasPrecision(18, 2);
        entity.Property(x => x.DailyRate).HasPrecision(18, 2);
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.NormalizedName).IsUnique().HasDatabaseName("UX_Rooms_NormalizedName");
    }
}
