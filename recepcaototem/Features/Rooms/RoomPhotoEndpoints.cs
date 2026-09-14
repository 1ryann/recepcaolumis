using GestaoPredio.Application.Abstractions;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;

namespace recepcaototem.Features.Rooms;

public static class RoomPhotoEndpoints
{
    public static IEndpointRouteBuilder MapRoomPhotoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/rooms/{roomId:guid}/photos")
            .RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/{photoId:guid}", Content);
        group.MapPost("", RoomPhotoMutation.UploadAsync).AddEndpointFilter<AntiforgeryFilter>();
        group.MapDelete("/{photoId:guid}", RoomPhotoMutation.DeleteAsync).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPut("/reorder", RoomPhotoMutation.ReorderAsync).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{photoId:guid}/cover", RoomPhotoMutation.SetCoverAsync).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(Guid roomId, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Rooms.AnyAsync(x => x.Id == roomId, cancellationToken)) return Results.NotFound();
        var photos = await db.RoomPhotos.AsNoTracking().Where(x => x.RoomId == roomId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return Results.Ok(photos.Select(photo => new RoomPhotoResponse(photo.Id,
            $"/api/admin/rooms/{roomId}/photos/{photo.Id}?v={photo.PrivateFileId}",
            photo.SortOrder, photo.IsCover, photo.CreatedAt)).ToArray());
    }

    private static async Task<IResult> Content(Guid roomId, Guid photoId, HttpContext context,
        ApplicationDbContext db, IPrivateFileStorage storage, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var photo = await db.RoomPhotos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RoomId == roomId && x.Id == photoId, cancellationToken);
        if (photo is null) return Results.NotFound();
        return await RoomPhotoStreaming.StreamAsync(photo, db, storage, loggerFactory, context,
            "private, no-store", publicFailureIsNotFound: false, cancellationToken);
    }
}
