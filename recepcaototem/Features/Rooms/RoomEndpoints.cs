using System.Security.Claims;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public static class RoomEndpoints
{
    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/rooms").RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPut("/{id:guid}", Update).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/activate", Activate).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/deactivate", Deactivate).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(int? page, int? pageSize, string? status, string? search,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!PagingQuery.TryCreate(page, pageSize, status, search, out var paging, out var error))
            return Results.BadRequest(error);
        var query = db.Rooms.AsNoTracking();
        query = paging!.Status switch
        {
            PagingQuery.Active => query.Where(x => x.IsActive),
            PagingQuery.Inactive => query.Where(x => !x.IsActive),
            _ => query
        };
        if (paging.Search is not null) query = query.Where(x => x.NormalizedName.Contains(paging.Search));
        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<RoomResponse>(
            entities.Select(x => x.ToResponse()).ToArray(), paging.Page, paging.PageSize, totalCount));
    }

    private static async Task<IResult> Detail(Guid id, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return room is null ? Results.NotFound() : Results.Ok(room.ToResponse());
    }

    private static async Task<IResult> Create(CreateRoomRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!RoomInput.TryValidate(request, out var input, out var error)) return Results.BadRequest(error);
        var normalizedName = TextNormalizer.Normalize(input!.Name);
        if (await db.Rooms.AnyAsync(x => x.NormalizedName == normalizedName, cancellationToken)) return NameConflict();

        var now = timeProvider.GetUtcNow();
        var room = Room.Create(input.Name, input.Description, input.HourlyRate, input.DailyRate, now);
        db.Rooms.Add(room);
        db.AuditEntries.Add(CreateAudit(context, room.Id, "ROOM_CREATED", now));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (SqlServerRoomErrors.IsNameConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return NameConflict();
        }
        return Results.Created($"/api/admin/rooms/{room.Id}", room.ToResponse());
    }

    private static async Task<IResult> Update(Guid id, UpdateRoomRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion)) return InvalidToken();
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (room is null) return Results.NotFound();
        if (!room.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return Modified();
        if (!RoomInput.TryValidate(request, out var input, out var error)) return Results.BadRequest(error);

        var normalizedName = TextNormalizer.Normalize(input!.Name);
        if (await db.Rooms.AnyAsync(x => x.Id != id && x.NormalizedName == normalizedName, cancellationToken))
            return NameConflict();

        var changedFields = new List<string>(4);
        if (!string.Equals(room.Name, input.Name, StringComparison.Ordinal)) changedFields.Add(AuditFields.Name);
        if (!string.Equals(room.Description, input.Description, StringComparison.Ordinal)) changedFields.Add(AuditFields.Description);
        if (room.HourlyRate != input.HourlyRate) changedFields.Add(AuditFields.HourlyRate);
        if (room.DailyRate != input.DailyRate) changedFields.Add(AuditFields.DailyRate);
        if (changedFields.Count == 0) return Results.Ok(room.ToResponse());

        db.Entry(room).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        room.Update(input.Name, input.Description, input.HourlyRate, input.DailyRate, now);
        var audit = CreateAudit(context, room.Id, "ROOM_UPDATED", now);
        audit.SetChangedFields(changedFields);
        db.AuditEntries.Add(audit);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        catch (DbUpdateException exception) when (SqlServerRoomErrors.IsNameConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return NameConflict();
        }
        return Results.Ok(room.ToResponse());
    }

    private static Task<IResult> Activate(Guid id, RoomConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        ChangeStatus(id, request, true, context, db, timeProvider, cancellationToken);

    private static Task<IResult> Deactivate(Guid id, RoomConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        ChangeStatus(id, request, false, context, db, timeProvider, cancellationToken);

    private static async Task<IResult> ChangeStatus(Guid id, RoomConcurrencyRequest request, bool isActive,
        HttpContext context, ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion)) return InvalidToken();
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (room is null) return Results.NotFound();
        if (!room.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return Modified();
        if (room.IsActive == isActive) return Results.Ok(room.ToResponse());

        db.Entry(room).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        if (isActive) room.Activate(now); else room.Deactivate(now);
        db.AuditEntries.Add(CreateAudit(context, room.Id,
            isActive ? "ROOM_ACTIVATED" : "ROOM_DEACTIVATED", now));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        return Results.Ok(room.ToResponse());
    }

    private static AuditEntry CreateAudit(HttpContext context, Guid id, string action, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
        IpAddress = context.Connection.RemoteIpAddress?.ToString(), Action = action, Result = "SUCCEEDED",
        OccurredAt = now, CorrelationId = context.TraceIdentifier, TargetEntityType = "ROOM", TargetEntityId = id
    };

    private static IResult NameConflict() => Results.Json(new ApiError(
        "ROOM_NAME_ALREADY_EXISTS", "Já existe uma sala com nome equivalente."), statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
}
