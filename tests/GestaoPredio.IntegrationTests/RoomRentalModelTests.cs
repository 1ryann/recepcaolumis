using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GestaoPredio.IntegrationTests;

public sealed class RoomRentalModelTests
{
    [Fact]
    public void Gallery_preserves_files_and_has_one_cover_per_room()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var photo = db.Model.FindEntityType(typeof(RoomPhoto));
        Assert.NotNull(photo);
        Assert.Equal("RoomPhotos", photo.GetTableName());
        Assert.Equal("Id", Assert.Single(photo.FindPrimaryKey()!.Properties).Name);
        Assert.Equal("timestamp with time zone", photo.FindProperty("CreatedAt")!.GetColumnType());
        Assert.Equal(new[] { "CreatedAt", "Id", "IsCover", "PrivateFileId", "RoomId", "SortOrder" },
            photo.GetProperties().Select(x => x.Name).Order().ToArray());
        Assert.Equal(2, photo.GetForeignKeys().Count());
        AssertForeignKey(photo, "RoomId", typeof(Room), DeleteBehavior.Cascade);
        AssertForeignKey(photo, "PrivateFileId", typeof(PrivateFile), DeleteBehavior.Restrict);
        var cover = Assert.Single(photo.GetIndexes(), x => x.GetDatabaseName() == "UX_RoomPhotos_Room_Cover");
        Assert.True(cover.IsUnique);
        Assert.Equal("RoomId", Assert.Single(cover.Properties).Name);
        Assert.Equal("\"IsCover\"", cover.GetFilter());
        var order = Assert.Single(photo.GetIndexes(), x => x.GetDatabaseName() == "IX_RoomPhotos_Room_SortOrder");
        Assert.Equal(new[] { "RoomId", "SortOrder" }, order.Properties.Select(x => x.Name));
        Assert.False(order.IsUnique);
    }

    [Fact]
    public void Inquiry_has_only_the_contracted_snapshot_and_conversion_columns()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var inquiry = db.Model.FindEntityType(typeof(RoomRentalInquiry));
        Assert.NotNull(inquiry);
        Assert.Equal("RoomRentalInquiries", inquiry.GetTableName());
        Assert.Equal("Id", Assert.Single(inquiry.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(new[] { "ConvertedAt", "CreatedAt", "FullName", "Id", "LeaseId", "Note",
                "PresentedAvailabilityStatus", "PresentedAvailableFrom", "ProfessionOrCompany", "RoomId", "Status", "WhatsApp" },
            inquiry.GetProperties().Select(x => x.Name).Order().ToArray());
        foreach (var (name, length, nullable) in new[]
                 { ("FullName", 200, false), ("ProfessionOrCompany", 200, false), ("WhatsApp", 16, false), ("Note", 500, true),
                     ("PresentedAvailabilityStatus", 20, false), ("Status", 10, false) })
        {
            var property = inquiry.FindProperty(name)!;
            Assert.Equal(length, property.GetMaxLength());
            Assert.Equal($"character varying({length})", property.GetColumnType());
            Assert.Equal(nullable, property.IsNullable);
        }
        Assert.True(inquiry.FindProperty("PresentedAvailableFrom")!.IsNullable);
        Assert.Equal("date", inquiry.FindProperty("PresentedAvailableFrom")!.GetColumnType());
        Assert.True(inquiry.FindProperty("LeaseId")!.IsNullable);
        Assert.True(inquiry.FindProperty("ConvertedAt")!.IsNullable);
        Assert.False(inquiry.FindProperty("CreatedAt")!.IsNullable);
        foreach (var name in new[] { "CreatedAt", "ConvertedAt" })
            Assert.Equal("timestamp with time zone", inquiry.FindProperty(name)!.GetColumnType());
        Assert.Empty(inquiry.GetNavigations());
        Assert.Equal(2, inquiry.GetForeignKeys().Count());
        AssertForeignKey(inquiry, "RoomId", typeof(Room), DeleteBehavior.NoAction);
        AssertForeignKey(inquiry, "LeaseId", typeof(Lease), DeleteBehavior.NoAction);
    }

    [Theory]
    [InlineData(PublicRoomAvailabilityStatus.AvailableNow, "AVAILABLE_NOW")]
    [InlineData(PublicRoomAvailabilityStatus.AvailableSoon, "AVAILABLE_SOON")]
    public void Snapshot_availability_round_trips_the_public_wire_values(PublicRoomAvailabilityStatus value, string stored)
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var inquiry = db.Model.FindEntityType(typeof(RoomRentalInquiry));
        Assert.NotNull(inquiry);
        var converter = inquiry.FindProperty("PresentedAvailabilityStatus")!.GetTypeMapping().Converter;
        Assert.NotNull(converter);
        Assert.Equal(stored, converter.ConvertToProvider(value));
        Assert.Equal(value, converter.ConvertFromProvider(stored));
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.ConvertToProvider((PublicRoomAvailabilityStatus)99));
    }

    [Theory]
    [InlineData(RoomRentalInquiryStatus.New, "NEW")]
    [InlineData(RoomRentalInquiryStatus.Converted, "CONVERTED")]
    public void Inquiry_status_round_trips_the_wire_values(RoomRentalInquiryStatus value, string stored)
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var inquiry = db.Model.FindEntityType(typeof(RoomRentalInquiry));
        Assert.NotNull(inquiry);
        var converter = inquiry.FindProperty("Status")!.GetTypeMapping().Converter;
        Assert.NotNull(converter);
        Assert.Equal(stored, converter.ConvertToProvider(value));
        Assert.Equal(value, converter.ConvertFromProvider(stored));
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.ConvertToProvider((RoomRentalInquiryStatus)99));
    }

    [Fact]
    public void Inquiry_queue_is_partial_and_conversion_requires_lease_and_timestamp_together()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var inquiry = db.Model.FindEntityType(typeof(RoomRentalInquiry));
        Assert.NotNull(inquiry);
        var queue = Assert.Single(inquiry.GetIndexes(), x => x.GetDatabaseName() == "IX_RoomRentalInquiries_Status_CreatedAt");
        Assert.Equal(new[] { "Status", "CreatedAt" }, queue.Properties.Select(x => x.Name));
        Assert.Equal("\"Status\" = 'NEW'", queue.GetFilter());
        Assert.False(queue.IsUnique);
        var history = Assert.Single(inquiry.GetIndexes(), x => x.GetDatabaseName() == "IX_RoomRentalInquiries_Room_CreatedAt");
        Assert.Equal(new[] { "RoomId", "CreatedAt" }, history.Properties.Select(x => x.Name));
        Assert.Null(history.GetFilter());
        var designModel = db.GetService<IDesignTimeModel>().Model;
        Assert.Equal("(\"Status\" = 'NEW' AND \"LeaseId\" IS NULL AND \"ConvertedAt\" IS NULL) OR (\"Status\" = 'CONVERTED' AND \"LeaseId\" IS NOT NULL AND \"ConvertedAt\" IS NOT NULL)",
            Assert.Single(designModel.FindEntityType(typeof(RoomRentalInquiry))!.GetCheckConstraints()).Sql);
        Assert.Equal("\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')",
            Assert.Single(designModel.FindEntityType(typeof(PrivateFile))!.GetCheckConstraints(), x => x.Name == "CK_PrivateFiles_Purpose").Sql);
    }

    private static void AssertForeignKey(IEntityType entity, string property, Type principal, DeleteBehavior behavior)
    {
        var foreignKey = Assert.Single(entity.GetForeignKeys(), x => x.Properties.Single().Name == property);
        Assert.Equal(principal, foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(behavior, foreignKey.DeleteBehavior);
    }
}
