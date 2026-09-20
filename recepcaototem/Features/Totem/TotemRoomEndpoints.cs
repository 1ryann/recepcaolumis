using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Rooms;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
                    ? PhotoUrl(room.Id, cover.Id, cover.PrivateFileId) : null,
                room.MonthlyRate, room.CapacityMin, room.CapacityMax,
                room.Category is { } category ? RoomCategoryCode.From(category) : null));
        }
        return Results.Ok(result);
    }

    private static async Task<IResult> Detail(Guid id, HttpContext context, CustomerPublicRateLimiter limiter,
        ApplicationDbContext db, IOptions<WhatsappOptions> whatsappOptions,
        TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
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
            availability.AvailableFrom, photos.Select(x => PhotoUrl(room.Id, x.Id, x.PrivateFileId)).ToArray(),
            room.MonthlyRate, Advertised(room.HourlyRate), Advertised(room.DailyRate),
            room.AreaSquareMeters, room.BathroomCount, room.CapacityMin, room.CapacityMax,
            room.Category is { } category ? RoomCategoryCode.From(category) : null,
            [.. room.Amenities.Select(RoomAmenityCode.From)],
            ReceptionWhatsappUrl(whatsappOptions, room.Name)));
    }

    /// Hourly and daily rates are required on a room and default to zero, so a room nobody
    /// has priced carries 0 rather than null. Advertising "R$ 0,00/hora" would be a lie, so
    /// zero is reported as no price at all.
    private static decimal? Advertised(decimal rate) => rate > 0m ? rate : null;

    /// The reception's WhatsApp with the room's name already typed in. Built here so the
    /// browser never learns the number or assembles a wa.me link, and null — rather than an
    /// error — when no number is configured, which Development legitimately does.
    private static string? ReceptionWhatsappUrl(IOptions<WhatsappOptions> options, string roomName)
    {
        string phone;
        try { phone = options.Value.FinanceiroPhoneNumber; }
        catch (OptionsValidationException) { return null; }

        var message = $"Olá! Tenho interesse na {roomName} do LUMIS.";
        return WhatsappLinkBuilder.TryBuild(phone, message, out var url) ? url : null;
    }

    private static async Task<IResult> Photo(Guid roomId, Guid photoId, HttpContext context,
        RoomPhotoRateLimiter limiter, ApplicationDbContext db, IPrivateFileStorage storage,
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
