using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class RoomRentalInquiryConfiguration : IEntityTypeConfiguration<RoomRentalInquiry>
{
    public void Configure(EntityTypeBuilder<RoomRentalInquiry> entity)
    {
        entity.ToTable("RoomRentalInquiries", table =>
            table.HasCheckConstraint("CK_RoomRentalInquiries_ConversionState",
                "(\"Status\" = 'NEW' AND \"LeaseId\" IS NULL AND \"ConvertedAt\" IS NULL) OR (\"Status\" = 'CONVERTED' AND \"LeaseId\" IS NOT NULL AND \"ConvertedAt\" IS NOT NULL)"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.ProfessionOrCompany).HasMaxLength(200).IsRequired();
        entity.Property(x => x.WhatsApp).HasMaxLength(16).IsRequired();
        entity.Property(x => x.Note).HasMaxLength(500);
        entity.Property(x => x.PresentedAvailabilityStatus)
            .HasConversion(new ValueConverter<PublicRoomAvailabilityStatus, string>(
                value => AvailabilityToStorage(value), value => AvailabilityFromStorage(value)))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.PresentedAvailableFrom).HasColumnType("date");
        entity.Property(x => x.Status)
            .HasConversion(new ValueConverter<RoomRentalInquiryStatus, string>(
                value => StatusToStorage(value), value => StatusFromStorage(value)))
            .HasMaxLength(10).IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ConvertedAt).HasColumnType("timestamp with time zone");
        entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.RoomId, x.CreatedAt }).HasDatabaseName("IX_RoomRentalInquiries_Room_CreatedAt");
        entity.HasIndex(x => new { x.Status, x.CreatedAt }).HasFilter("\"Status\" = 'NEW'")
            .HasDatabaseName("IX_RoomRentalInquiries_Status_CreatedAt");
    }

    private static string AvailabilityToStorage(PublicRoomAvailabilityStatus value) => value switch
    {
        PublicRoomAvailabilityStatus.AvailableNow => "AVAILABLE_NOW",
        PublicRoomAvailabilityStatus.AvailableSoon => "AVAILABLE_SOON",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid room availability status.")
    };

    private static PublicRoomAvailabilityStatus AvailabilityFromStorage(string value) => value switch
    {
        "AVAILABLE_NOW" => PublicRoomAvailabilityStatus.AvailableNow,
        "AVAILABLE_SOON" => PublicRoomAvailabilityStatus.AvailableSoon,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid stored room availability status.")
    };

    private static string StatusToStorage(RoomRentalInquiryStatus value) => value switch
    {
        RoomRentalInquiryStatus.New => "NEW",
        RoomRentalInquiryStatus.Converted => "CONVERTED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid rental inquiry status.")
    };

    private static RoomRentalInquiryStatus StatusFromStorage(string value) => value switch
    {
        "NEW" => RoomRentalInquiryStatus.New,
        "CONVERTED" => RoomRentalInquiryStatus.Converted,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid stored rental inquiry status.")
    };
}
