using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class RoomPhotoDomainTests
{
    [Fact]
    public void Attach_keeps_valid_photo_data()
    {
        var roomId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 14, 10, 30, 0, TimeSpan.FromHours(-4)).AddTicks(7);

        var photo = RoomPhoto.Attach(roomId, fileId, 0, true, occurredAt);

        Assert.NotEqual(Guid.Empty, photo.Id);
        Assert.Equal(roomId, photo.RoomId);
        Assert.Equal(fileId, photo.PrivateFileId);
        Assert.Equal(0, photo.SortOrder);
        Assert.True(photo.IsCover);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 14, 30, 0, TimeSpan.Zero), photo.CreatedAt);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, -1)]
    public void Attach_rejects_missing_ids_and_negative_sort_order(bool emptyRoomId, bool emptyFileId, int sortOrder)
    {
        var roomId = emptyRoomId ? Guid.Empty : Guid.NewGuid();
        var fileId = emptyFileId ? Guid.Empty : Guid.NewGuid();

        Assert.ThrowsAny<ArgumentException>(() => RoomPhoto.Attach(roomId, fileId, sortOrder, false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reorder_rejects_a_negative_sort_order()
    {
        var photo = CreatePhoto();

        Assert.Throws<ArgumentOutOfRangeException>(() => photo.Reorder(-1));
    }

    [Fact]
    public void Reorder_updates_sort_order()
    {
        var photo = CreatePhoto();

        photo.Reorder(2);

        Assert.Equal(2, photo.SortOrder);
    }

    [Fact]
    public void Set_cover_updates_cover_state()
    {
        var photo = CreatePhoto();

        photo.SetCover(true);
        Assert.True(photo.IsCover);

        photo.SetCover(false);
        Assert.False(photo.IsCover);
    }

    private static RoomPhoto CreatePhoto() =>
        RoomPhoto.Attach(Guid.NewGuid(), Guid.NewGuid(), 0, false, DateTimeOffset.UtcNow);
}
