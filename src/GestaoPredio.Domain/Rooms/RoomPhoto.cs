using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Rooms;

public sealed class RoomPhoto
{
    private RoomPhoto()
    {
    }

    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid PrivateFileId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsCover { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static RoomPhoto Attach(Guid roomId, Guid privateFileId, int sortOrder, bool isCover, DateTimeOffset occurredAt)
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException("A sala deve ser informada.", nameof(roomId));
        }

        if (privateFileId == Guid.Empty)
        {
            throw new ArgumentException("O arquivo deve ser informado.", nameof(privateFileId));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        return new RoomPhoto
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            PrivateFileId = privateFileId,
            SortOrder = sortOrder,
            IsCover = isCover,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt)
        };
    }

    public void Reorder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        SortOrder = sortOrder;
    }

    public void SetCover(bool isCover) => IsCover = isCover;
}
