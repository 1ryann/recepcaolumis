using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public sealed record RoomPhotoResponse(Guid Id, string PhotoUrl, int SortOrder, bool IsCover, DateTimeOffset CreatedAt);

public sealed record ReorderRoomPhotosRequest(IReadOnlyList<Guid>? OrderedPhotoIds) : IStrictModuleRequest;
