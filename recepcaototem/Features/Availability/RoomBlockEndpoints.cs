using System.Security.Claims;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Availability;

public static class RoomBlockEndpoints
{
    public static IEndpointRouteBuilder MapRoomBlockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/room-blocks").RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPut("/{id:guid}", Update).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/cancel", Cancel).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(int? page, int? pageSize, string? status,
        Guid? roomId, DateTimeOffset? from, DateTimeOffset? to,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var actualPage = page ?? 1;
        var actualSize = pageSize ?? 20;
        if (actualPage < 1) return Bad("INVALID_PAGE", "A página deve ser maior ou igual a 1.");
        if (actualSize is < 1 or > 100) return Bad("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100.");
        if (roomId == Guid.Empty || from >= to) return Bad("INVALID_ROOM_BLOCK_FILTER", "O filtro informado é inválido.");
        var actualStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToUpperInvariant();
        if (actualStatus == "ALL") actualStatus = "all";
        if (actualStatus is not ("all" or "ACTIVE" or "CANCELLED"))
            return Bad("INVALID_STATUS", "O status informado é inválido.");

        var query = db.RoomBlocks.AsNoTracking();
        if (actualStatus != "all")
        {
            var parsed = actualStatus == "ACTIVE" ? RoomBlockStatus.Active : RoomBlockStatus.Cancelled;
            query = query.Where(value => value.Status == parsed);
        }
        if (roomId is not null) query = query.Where(value => value.RoomId == roomId);
        if (from is not null) query = query.Where(value => value.EndAt > from);
        if (to is not null) query = query.Where(value => value.StartAt < to);
        var joined = from block in query
                     join room in db.Rooms.AsNoTracking() on block.RoomId equals room.Id
                     select new { Block = block, RoomName = room.Name };
        var total = await joined.CountAsync(cancellationToken);
        var rows = await joined.OrderBy(value => value.Block.StartAt).ThenBy(value => value.Block.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<RoomBlockResponse>(
            rows.Select(value => ToResponse(value.Block, value.RoomName)).ToArray(), actualPage, actualSize, total));
    }

    private static async Task<IResult> Detail(Guid id, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var row = await (from block in db.RoomBlocks.AsNoTracking().Where(value => value.Id == id)
                         join room in db.Rooms.AsNoTracking() on block.RoomId equals room.Id
                         select new { Block = block, RoomName = room.Name }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? Results.NotFound() : Results.Ok(ToResponse(row.Block, row.RoomName));
    }

    private static async Task<IResult> Create(CreateRoomBlockRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, IRoomAvailabilityService availability,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (request.RoomId == Guid.Empty || request.EndAt <= request.StartAt) return Invalid();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [request.RoomId], []), cancellationToken);
        var roomName = await ActiveRoomName(db, request.RoomId, cancellationToken);
        if (roomName is null) return InvalidResource();
        if (await availability.CheckBlockConflictsAsync(request.RoomId, request.StartAt, request.EndAt,
                null, cancellationToken) != RoomAvailabilityConflict.None) return OccupancyConflict();
        RoomBlock block;
        try { block = RoomBlock.Create(request.RoomId, request.StartAt, request.EndAt, request.Reason!, Actor(context)!, now); }
        catch (ArgumentException) { return Invalid(); }
        db.RoomBlocks.Add(block);
        db.AuditEntries.Add(AvailabilityAudit.CreateSucceeded(block.Id, AuditTargetTypes.RoomBlock,
            AuditActions.RoomBlockCreated, now, context.TraceIdentifier, Actor(context),
            context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/admin/room-blocks/{block.Id}", ToResponse(block, roomName));
    }

    private static async Task<IResult> Update(Guid id, UpdateRoomBlockRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, IRoomAvailabilityService availability,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        if (request.EndAt <= request.StartAt) return Invalid();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var block = await db.RoomBlocks.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (block is null) return Results.NotFound();
        if (block.Version != version) return Modified();
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [block.RoomId], []), cancellationToken);
        var roomName = await ActiveRoomName(db, block.RoomId, cancellationToken);
        if (roomName is null) return InvalidResource();
        if (await availability.CheckBlockConflictsAsync(block.RoomId, request.StartAt, request.EndAt,
                block.Id, cancellationToken) != RoomAvailabilityConflict.None) return OccupancyConflict();
        db.Entry(block).Property(value => value.Version).OriginalValue = version;
        try { block.Update(request.StartAt, request.EndAt, request.Reason!, now); }
        catch (ArgumentException) { return Invalid(); }
        catch (InvalidOperationException) { return InvalidState(); }
        db.AuditEntries.Add(AvailabilityAudit.CreateSucceeded(block.Id, AuditTargetTypes.RoomBlock,
            AuditActions.RoomBlockUpdated, now, context.TraceIdentifier, Actor(context),
            context.Connection.RemoteIpAddress?.ToString()));
        return await Save(block, roomName, db, transaction, cancellationToken);
    }

    private static async Task<IResult> Cancel(Guid id, RoomBlockConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var block = await db.RoomBlocks.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (block is null) return Results.NotFound();
        if (block.Version != version) return Modified();
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [block.RoomId], []), cancellationToken);
        var roomName = await db.Rooms.AsNoTracking().Where(value => value.Id == block.RoomId)
            .Select(value => value.Name).SingleAsync(cancellationToken);
        db.Entry(block).Property(value => value.Version).OriginalValue = version;
        try { block.Cancel(Actor(context)!, now); }
        catch (InvalidOperationException) { return InvalidState(); }
        db.AuditEntries.Add(AvailabilityAudit.CreateSucceeded(block.Id, AuditTargetTypes.RoomBlock,
            AuditActions.RoomBlockCancelled, now, context.TraceIdentifier, Actor(context),
            context.Connection.RemoteIpAddress?.ToString()));
        return await Save(block, roomName, db, transaction, cancellationToken);
    }

    private static async Task<IResult> Save(RoomBlock block, string roomName, ApplicationDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
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
        return Results.Ok(ToResponse(block, roomName));
    }

    private static RoomBlockResponse ToResponse(RoomBlock block, string roomName) => new(
        block.Id, block.RoomId, roomName, block.StartAt, block.EndAt, block.Reason,
        block.Status == RoomBlockStatus.Active ? "ACTIVE" : "CANCELLED", block.CreatedAt,
        block.UpdatedAt, block.CancelledAt, ConcurrencyToken.Encode(block.Version));
    private static Task<string?> ActiveRoomName(ApplicationDbContext db, Guid roomId,
        CancellationToken cancellationToken) => db.Rooms.AsNoTracking()
        .Where(value => value.Id == roomId && value.IsActive).Select(value => value.Name)
        .SingleOrDefaultAsync(cancellationToken);
    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult Bad(string code, string message) => Results.BadRequest(new ApiError(code, message));
    private static IResult Invalid() => Bad("INVALID_ROOM_BLOCK", "O bloqueio de sala informado é inválido.");
    private static IResult InvalidResource() => Bad("INVALID_ROOM_BLOCK_RESOURCE", "A sala informada é inválida.");
    private static IResult InvalidToken() => Bad("INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido.");
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidState() => Results.Json(new ApiError(
        "INVALID_ROOM_BLOCK_STATE", "O bloqueio não permite esta operação no estado atual."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult OccupancyConflict() => Results.Json(new ApiError(
        "ROOM_BLOCK_OCCUPANCY_CONFLICT", "A sala possui reserva, locação ou bloqueio conflitante."),
        statusCode: StatusCodes.Status409Conflict);
}
