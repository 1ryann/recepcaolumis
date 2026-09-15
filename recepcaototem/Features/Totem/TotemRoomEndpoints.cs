using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Rooms;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;
using recepcaototem.Features.Customers;
using recepcaototem.Features.Rooms;

namespace recepcaototem.Features.Totem;

public static class TotemRoomEndpoints
{
    public static IEndpointRouteBuilder MapTotemRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/totem/rooms", List).AllowAnonymous();
        endpoints.MapGet("/api/totem/rooms/{id:guid}", Detail).AllowAnonymous();
        endpoints.MapGet("/api/totem/rooms/{roomId:guid}/photos/{photoId:guid}", Photo).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> List(HttpContext context, CustomerPublicRateLimiter limiter,
        ApplicationDbContext db, TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        using var rate = await limiter.AcquireAsync(RemoteIp(context), "rooms", ct);
        if (!rate.IsAcquired) return TooManyRequests();

        var rooms = await db.Rooms.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).ToListAsync(ct);
        if (rooms.Count == 0) return Results.Ok(Array.Empty<PublicRoomCard>());

        var roomIds = rooms.Select(x => x.Id).ToArray();
        var leases = await db.Leases.AsNoTracking().Where(x => roomIds.Contains(x.RoomId)).ToListAsync(ct);
        var leasesByRoom = leases.GroupBy(x => x.RoomId).ToDictionary(x => x.Key, x => x.AsEnumerable());
        var covers = await db.RoomPhotos.AsNoTracking().Where(x => roomIds.Contains(x.RoomId) && x.IsCover)
            .Select(x => new { x.RoomId, x.Id, x.PrivateFileId }).ToListAsync(ct);
        var coversByRoom = covers.ToDictionary(x => x.RoomId);
        var now = time.GetUtcNow();
        var result = new List<PublicRoomCard>(rooms.Count);
        foreach (var room in rooms)
        {
            var availability = RoomAvailabilityCalculator.Calculate(
                leasesByRoom.TryGetValue(room.Id, out var roomLeases) ? roomLeases : [], now, timeZone);
            if (availability is null) continue;
            result.Add(new PublicRoomCard(room.Id, room.Name, room.Description, availability.Status,
                availability.AvailableFrom, coversByRoom.TryGetValue(room.Id, out var cover)
                    ? PhotoUrl(room.Id, cover.Id, cover.PrivateFileId) : null));
        }
        return Results.Ok(result);
    }

    private static async Task<IResult> Detail(Guid id, HttpContext context, CustomerPublicRateLimiter limiter,
        ApplicationDbContext db, TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        using var rate = await limiter.AcquireAsync(RemoteIp(context), $"room:{id}", ct);
        if (!rate.IsAcquired) return TooManyRequests();

        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (room is null) return Results.NotFound();
        var availability = RoomAvailabilityCalculator.Calculate(
            await db.Leases.AsNoTracking().Where(x => x.RoomId == id).ToListAsync(ct), time.GetUtcNow(), timeZone);
        if (availability is null) return Results.NotFound();
        var photos = await db.RoomPhotos.AsNoTracking().Where(x => x.RoomId == id)
            .OrderByDescending(x => x.IsCover).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.PrivateFileId }).ToListAsync(ct);
        return Results.Ok(new PublicRoomDetail(room.Id, room.Name, room.Description, availability.Status,
            availability.AvailableFrom, photos.Select(x => PhotoUrl(room.Id, x.Id, x.PrivateFileId)).ToArray()));
    }

    private static async Task<IResult> Photo(Guid roomId, Guid photoId, HttpContext context,
        CustomerPublicRateLimiter limiter, ApplicationDbContext db, IPrivateFileStorage storage,
        ILoggerFactory loggerFactory, TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        using var rate = await limiter.AcquireAsync(RemoteIp(context), $"room-photo:{roomId}:{photoId}", ct);
        if (!rate.IsAcquired) return TooManyRequests();

        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == roomId && x.IsActive, ct);
        if (room is null) return Results.NotFound();
        var availability = RoomAvailabilityCalculator.Calculate(
            await db.Leases.AsNoTracking().Where(x => x.RoomId == roomId).ToListAsync(ct), time.GetUtcNow(), timeZone);
        if (availability is null) return Results.NotFound();
        var photo = await db.RoomPhotos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RoomId == roomId && x.Id == photoId, ct);
        if (photo is null) return Results.NotFound();
        return await RoomPhotoStreaming.StreamAsync(photo, db, storage, loggerFactory, context,
            "public, max-age=3600", publicFailureIsNotFound: true, ct);
    }

    private static string RemoteIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static string PhotoUrl(Guid roomId, Guid photoId, Guid privateFileId) =>
        $"/api/totem/rooms/{roomId}/photos/{photoId}?v={privateFileId}";
    private static IResult TooManyRequests() => Results.Json(
        new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: StatusCodes.Status429TooManyRequests);
}
