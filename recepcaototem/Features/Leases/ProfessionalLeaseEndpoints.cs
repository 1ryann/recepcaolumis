using System.Security.Claims;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Leases;

public static class ProfessionalLeaseEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalLeaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/leases").RequireAuthorization("Professional");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actualPage = page ?? 1;
        if (actualPage < 1) return Results.BadRequest(new ApiError("INVALID_PAGE", "A página deve ser maior ou igual a 1."));
        var actualSize = pageSize ?? 20;
        if (actualSize is < 1 or > 100) return Results.BadRequest(new ApiError("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100."));

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var professionalId = await db.Professionals.AsNoTracking()
            .Where(x => x.ApplicationUserId == userId && x.IsActive)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (professionalId is null)
            return Results.Ok(new PagedResponse<ProfessionalLeaseResponse>([], actualPage, actualSize, 0));

        var query =
            from lease in db.Leases.AsNoTracking().Where(x => x.ProfessionalId == professionalId)
            join tenant in db.Tenants.AsNoTracking() on lease.TenantId equals tenant.Id
            join room in db.Rooms.AsNoTracking() on lease.RoomId equals room.Id
            select new { Lease = lease, TenantName = tenant.Name, RoomName = room.Name };
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.Lease.OccupancyStartAt).ThenBy(x => x.Lease.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        return Results.Ok(new PagedResponse<ProfessionalLeaseResponse>(
            rows.Select(x => ToResponse(x.Lease, x.TenantName, x.RoomName, now)).ToArray(),
            actualPage, actualSize, totalCount));
    }

    private static async Task<IResult> Detail(
        Guid id,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var row = await (
            from professional in db.Professionals.AsNoTracking()
            where professional.ApplicationUserId == userId && professional.IsActive
            join lease in db.Leases.AsNoTracking().Where(x => x.Id == id) on professional.Id equals lease.ProfessionalId
            join tenant in db.Tenants.AsNoTracking() on lease.TenantId equals tenant.Id
            join room in db.Rooms.AsNoTracking() on lease.RoomId equals room.Id
            select new { Lease = lease, TenantName = tenant.Name, RoomName = room.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(row.Lease, row.TenantName, row.RoomName, timeProvider.GetUtcNow()));
    }

    private static ProfessionalLeaseResponse ToResponse(Lease lease, string tenantName, string roomName, DateTimeOffset now) => new(
        lease.Id, tenantName, lease.RoomId, roomName, lease.Mode.ToContract(), lease.ContractedRate,
        lease.BillingStartAt, lease.BillingDueDay, lease.OccupancyStartAt, lease.OccupancyEndAt,
        lease.GetOperationalStatus(now).ToContract());
}

public sealed record ProfessionalLeaseResponse(
    Guid Id,
    string TenantName,
    Guid RoomId,
    string RoomName,
    string Mode,
    decimal ContractedRate,
    DateTimeOffset BillingStartAt,
    int? BillingDueDay,
    DateTimeOffset OccupancyStartAt,
    DateTimeOffset? OccupancyEndAt,
    string Status);
