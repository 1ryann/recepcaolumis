using System.Globalization;
using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Rooms;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public static class RoomRentalInquiryEndpoints
{
    public static IEndpointRouteBuilder MapRoomRentalInquiryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/totem/rooms/{roomId:guid}/rental-inquiries", Create).AllowAnonymous();
        endpoints.MapGet("/api/admin/room-rental-inquiries", ListAdmin).RequireAuthorization("Operations");
        endpoints.MapGet("/api/admin/room-rental-inquiries/{id:guid}", DetailAdmin).RequireAuthorization("Operations");
        return endpoints;
    }

    private static async Task<IResult> Create(Guid roomId, RoomRentalInquiryRequest request, HttpContext context,
        RoomRentalInquiryRateLimiter limiter, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        IOptions<WhatsappOptions> whatsappOptions, TimeZoneInfo timeZone, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Rate-limit identifier: normalized WhatsApp when it normalizes, otherwise the trimmed raw value —
        // the request has not been validated yet at this point, only used to key the budget.
        var identifier = WhatsAppNormalizer.TryNormalize(request.WhatsApp, out var normalizedForRateLimit)
            ? normalizedForRateLimit : request.WhatsApp?.Trim() ?? "";
        using var rate = await limiter.AcquireAsync(RemoteIp(context), identifier, cancellationToken);
        if (!rate.IsAcquired) return TooManyRequests();

        if (!RoomRentalInquiryInput.TryValidate(request, out var input, out var error))
            return Results.BadRequest(error);

        // Fail fast, before touching the database, if the finance WhatsApp number is not configured
        // (Development can legitimately run without it). The message content does not affect whether
        // the configured phone itself normalizes, so an empty placeholder is enough to check here.
        if (!WhatsappLinkBuilder.TryBuild(whatsappOptions.Value.FinanceiroPhoneNumber, "", out _))
            return WhatsappNotConfigured();

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), cancellationToken);

        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == roomId && x.IsActive, cancellationToken);
        if (room is null) return Results.NotFound();

        // Never trust anything from the client about availability (it is not even part of the request
        // body): recompute it live, under the resource lock, from the current Room + Lease state.
        var availability = RoomAvailabilityCalculator.Calculate(
            await db.Leases.AsNoTracking().Where(x => x.RoomId == roomId).ToListAsync(cancellationToken), now, timeZone);
        if (availability is null) return Results.NotFound();

        var inquiry = RoomRentalInquiry.Create(roomId, input!.FullName, input.WhatsApp, input.ProfessionOrCompany,
            input.Note, availability.Status, availability.AvailableFrom, now,
            input.DesiredStartDate, input.DesiredEndDate);
        db.RoomRentalInquiries.Add(inquiry);
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            Action = AuditActions.RoomRentalInquiryCreated,
            Result = "SUCCEEDED",
            OccurredAt = now,
            CorrelationId = context.TraceIdentifier,
            TargetEntityType = AuditTargetTypes.RoomRentalInquiry,
            TargetEntityId = inquiry.Id
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Only now — after the snapshot is committed — format the PT-BR label and build the WhatsApp
        // message/URL. Neither the request, the message, nor the URL is ever logged.
        var label = RoomAvailabilityFormatter.Format(availability.Status, availability.AvailableFrom);
        var desiredPeriod = $"{input.DesiredStartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} até " +
            input.DesiredEndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var message = "Olá! Tenho interesse em alugar uma sala na Lumis.\n\n" +
            $"Sala: {room.Name}\nDisponibilidade: {label}\nPeríodo desejado: {desiredPeriod}\nNome: {input.FullName}\n" +
            $"WhatsApp: {input.WhatsApp}\nProfissão/Empresa: {input.ProfessionOrCompany}\n" +
            $"Observação: {input.Note ?? "—"}";
        WhatsappLinkBuilder.TryBuild(whatsappOptions.Value.FinanceiroPhoneNumber, message, out var url);
        return Results.Ok(new RoomRentalInquiryResult(inquiry.Id, url, label));
    }

    // Deliberately separate from the public Create route above: these two are authenticated Admin reads
    // (Task 12) with no accompanying mutation endpoint — conversion to a Lease (Task 13) is the only way
    // an inquiry's Status/LeaseId/ConvertedAt ever change, and it happens through POST /api/admin/leases.
    private static async Task<IResult> ListAdmin(int? page, int? pageSize, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!PagingQuery.TryCreate(page, pageSize, null, null, out var paging, out var error))
            return Results.BadRequest(error);

        var query = from inquiry in db.RoomRentalInquiries.AsNoTracking()
                    join room in db.Rooms.AsNoTracking() on inquiry.RoomId equals room.Id
                    select new { Inquiry = inquiry, RoomName = room.Name };
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.Inquiry.CreatedAt).ThenByDescending(x => x.Inquiry.Id)
            .Skip((paging!.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(cancellationToken);

        var items = rows.Select(row => row.Inquiry.ToAdminResponse(row.RoomName)).ToArray();
        return Results.Ok(new PagedResponse<RoomRentalInquiryAdminResponse>(items, paging.Page, paging.PageSize, totalCount));
    }

    private static async Task<IResult> DetailAdmin(Guid id, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var row = await (from inquiry in db.RoomRentalInquiries.AsNoTracking()
                          join room in db.Rooms.AsNoTracking() on inquiry.RoomId equals room.Id
                          where inquiry.Id == id
                          select new { Inquiry = inquiry, RoomName = room.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? Results.NotFound() : Results.Ok(row.Inquiry.ToAdminResponse(row.RoomName));
    }

    private static string RemoteIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static IResult TooManyRequests() => Results.Json(
        new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: StatusCodes.Status429TooManyRequests);

    private static IResult WhatsappNotConfigured() => Results.Json(
        new ApiError("ROOM_RENTAL_WHATSAPP_NOT_CONFIGURED", "O WhatsApp do financeiro não está configurado."),
        statusCode: StatusCodes.Status503ServiceUnavailable);
}

internal static class RoomRentalInquiryAdminMappings
{
    public static RoomRentalInquiryAdminResponse ToAdminResponse(this RoomRentalInquiry inquiry, string roomName) => new(
        inquiry.Id,
        inquiry.RoomId,
        roomName,
        inquiry.FullName,
        inquiry.WhatsApp,
        inquiry.ProfessionOrCompany,
        inquiry.Note,
        inquiry.PresentedAvailabilityStatus,
        inquiry.PresentedAvailableFrom,
        RoomAvailabilityFormatter.Format(inquiry.PresentedAvailabilityStatus, inquiry.PresentedAvailableFrom),
        inquiry.Status.ToContract(),
        inquiry.LeaseId,
        inquiry.ConvertedAt,
        inquiry.CreatedAt,
        inquiry.DesiredStartDate,
        inquiry.DesiredEndDate);

    private static string ToContract(this RoomRentalInquiryStatus status) => status switch
    {
        RoomRentalInquiryStatus.New => "NEW",
        RoomRentalInquiryStatus.Converted => "CONVERTED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status de interesse desconhecido.")
    };
}
