using GestaoPredio.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> entity)
    {
        entity.ToTable("Rooms", table =>
        {
            table.HasCheckConstraint("CK_Rooms_HourlyRate_NonNegative", "\"HourlyRate\" >= 0");
            table.HasCheckConstraint("CK_Rooms_DailyRate_NonNegative", "\"DailyRate\" >= 0");
            // The public-catalogue attributes. All nullable: a room priced by the hour and
            // never measured is a normal room, not a broken row. The constraints mirror
            // RoomFeatures so a bad value cannot reach the table by any route.
            table.HasCheckConstraint("CK_Rooms_MonthlyRate_NonNegative",
                "\"MonthlyRate\" IS NULL OR \"MonthlyRate\" >= 0");
            table.HasCheckConstraint("CK_Rooms_Area_Positive",
                "\"AreaSquareMeters\" IS NULL OR (\"AreaSquareMeters\" > 0 AND \"AreaSquareMeters\" < 100000)");
            table.HasCheckConstraint("CK_Rooms_BathroomCount",
                "\"BathroomCount\" IS NULL OR \"BathroomCount\" BETWEEN 0 AND 20");
            table.HasCheckConstraint("CK_Rooms_Capacity",
                "(\"CapacityMin\" IS NULL AND \"CapacityMax\" IS NULL) OR (\"CapacityMin\" BETWEEN 1 AND 200 "
                + "AND (\"CapacityMax\" IS NULL OR (\"CapacityMax\" >= \"CapacityMin\" AND \"CapacityMax\" <= 200)))");
            table.HasCheckConstraint("CK_Rooms_Category",
                "\"Category\" IS NULL OR \"Category\" IN ('CONSULTORIO', 'REUNIAO', 'CRIATIVA')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.HourlyRate).HasPrecision(18, 2);
        entity.Property(x => x.DailyRate).HasPrecision(18, 2);
        entity.Property(x => x.MonthlyRate).HasPrecision(18, 2);
        entity.Property(x => x.AreaSquareMeters).HasPrecision(8, 2);
        entity.Property(x => x.BathroomCount).HasColumnType("smallint");
        entity.Property(x => x.CapacityMin).HasColumnType("smallint");
        entity.Property(x => x.CapacityMax).HasColumnType("smallint");
        entity.Property(x => x.Category)
            .HasConversion(new ValueConverter<RoomCategory?, string?>(
                value => value == null ? null : RoomCategoryCode.From(value.Value),
                value => RoomCategoryCode.ParseOrNull(value)))
            .HasMaxLength(20);
        // A Postgres text[] of RoomAmenityCode values. An array rather than a child table:
        // the set is small, it is only ever read whole with its room, and a new amenity is
        // then a new code instead of a migration.
        entity.Property<List<string>>("_amenityCodes")
            .HasColumnName("Amenities")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.NormalizedName).IsUnique().HasDatabaseName("UX_Rooms_NormalizedName");
    }
}
