using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Tenants;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/tenants").RequireAuthorization("Operations");
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
        var query = db.Tenants.AsNoTracking();
        query = paging!.Status switch
        {
            PagingQuery.Active => query.Where(x => x.IsActive),
            PagingQuery.Inactive => query.Where(x => !x.IsActive),
            _ => query
        };
        if (paging.Search is not null) query = query.Where(x => x.NormalizedName.Contains(paging.Search));
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize)
            .ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<TenantResponse>(
            items.Select(x => x.ToResponse()).ToArray(), paging.Page, paging.PageSize, totalCount));
    }

    private static async Task<IResult> Detail(Guid id, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return tenant is null ? Results.NotFound() : Results.Ok(tenant.ToResponse());
    }

    private static async Task<IResult> Create(CreateTenantRequest request, ApplicationDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!TenantInput.TryValidate(request.Name, request.Kind, out var name, out var kind)) return Invalid();
        var tenant = Tenant.Create(name, kind, timeProvider.GetUtcNow());
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/admin/tenants/{tenant.Id}", tenant.ToResponse());
    }

    private static async Task<IResult> Update(Guid id, UpdateTenantRequest request, ApplicationDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tenant is null) return Results.NotFound();
        if (tenant.Version != version) return Modified();
        if (!TenantInput.TryValidate(request.Name, request.Kind, out var name, out var kind)) return Invalid();
        if (tenant.Name == name && tenant.Kind == kind) return Results.Ok(tenant.ToResponse());
        db.Entry(tenant).Property(x => x.Version).OriginalValue = version;
        tenant.Update(name, kind, timeProvider.GetUtcNow());
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Modified(); }
        return Results.Ok(tenant.ToResponse());
    }

    private static Task<IResult> Activate(Guid id, TenantConcurrencyRequest request, ApplicationDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken) =>
        ChangeStatus(id, request, true, db, timeProvider, cancellationToken);

    private static Task<IResult> Deactivate(Guid id, TenantConcurrencyRequest request, ApplicationDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken) =>
        ChangeStatus(id, request, false, db, timeProvider, cancellationToken);

    private static async Task<IResult> ChangeStatus(Guid id, TenantConcurrencyRequest request, bool active,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tenant is null) return Results.NotFound();
        if (tenant.Version != version) return Modified();
        if (tenant.IsActive == active) return Results.Ok(tenant.ToResponse());
        db.Entry(tenant).Property(x => x.Version).OriginalValue = version;
        if (active) tenant.Activate(timeProvider.GetUtcNow()); else tenant.Deactivate(timeProvider.GetUtcNow());
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Modified(); }
        return Results.Ok(tenant.ToResponse());
    }

    private static IResult Invalid() => Results.BadRequest(new ApiError(
        "INVALID_TENANT", "Os dados do locatário são inválidos."));
    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
}
