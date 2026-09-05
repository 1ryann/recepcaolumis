using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public static class ProfessionalUserLinkEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalUserLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/professionals").RequireAuthorization("Administration");
        group.MapGet("/eligible-users", EligibleUsers);
        group.MapGet("/{id:guid}/user-link", GetLink);
        group.MapPut("/{id:guid}/user-link", PutLink).AddEndpointFilter<AntiforgeryFilter>();
        group.MapDelete("/{id:guid}/user-link", DeleteLink).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> EligibleUsers(int? page, int? pageSize, string? search,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!PagingQuery.TryCreate(page, pageSize, PagingQuery.All, search, out var paging, out var error))
            return Results.BadRequest(error);
        var query = EligibleUserQuery.Create(db).AsNoTracking();
        if (paging!.Search is not null)
        {
            var term = paging.Search;
            query = query.Where(user =>
                EF.Functions.Collate(user.DisplayName, EligibleUserQuery.Collation).Contains(term) ||
                EF.Functions.Collate(user.Email!, EligibleUserQuery.Collation).Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => EF.Functions.Collate(user.DisplayName, EligibleUserQuery.Collation))
            .ThenBy(user => user.Id)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .Select(user => new EligibleUserResponse(user.Id, user.DisplayName, user.Email!))
            .ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<EligibleUserResponse>(users, paging.Page, paging.PageSize, totalCount));
    }

    private static async Task<IResult> GetLink(Guid id, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        if (professional.ApplicationUserId is null) return Results.Ok(new ProfessionalUserLinkResponse(false));
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == professional.ApplicationUserId, cancellationToken);
        return Results.Ok(new ProfessionalUserLinkResponse(true, user.Id, user.DisplayName, user.Email));
    }

    private static async Task<IResult> PutLink(Guid id, ProfessionalUserLinkRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
            return ProfessionalEndpoints.InvalidToken();
        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        if (!professional.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return ProfessionalEndpoints.Modified();
        if (string.IsNullOrWhiteSpace(request.ApplicationUserId)) return InvalidUser();

        var userId = request.ApplicationUserId;
        if (!await EligibleUserQuery.WithRequiredRoleProfile(db).AnyAsync(x => x.Id == userId, cancellationToken))
            return InvalidUser();
        if (await db.Professionals.AnyAsync(x => x.Id != id && x.ApplicationUserId == userId, cancellationToken))
            return UserAlreadyLinked();
        if (string.Equals(professional.ApplicationUserId, userId, StringComparison.Ordinal))
            return Results.Ok(professional.ToResponse());

        var action = professional.ApplicationUserId is null
            ? "PROFESSIONAL_USER_LINKED"
            : "PROFESSIONAL_USER_REPLACED";
        db.Entry(professional).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        professional.LinkUser(userId, now);
        var audit = ProfessionalEndpoints.CreateAudit(context, professional.Id, action, now);
        audit.TargetUserId = userId;
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
            return ProfessionalEndpoints.Modified();
        }
        catch (DbUpdateException exception) when (SqlServerProfessionalErrors.IsUserLinkConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return UserAlreadyLinked();
        }
        return Results.Ok(professional.ToResponse());
    }

    private static async Task<IResult> DeleteLink(Guid id, [FromBody] ProfessionalUserUnlinkRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
            return ProfessionalEndpoints.InvalidToken();
        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        if (!professional.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return ProfessionalEndpoints.Modified();
        if (professional.ApplicationUserId is null) return Results.Ok(professional.ToResponse());

        var previousUserId = professional.ApplicationUserId;
        db.Entry(professional).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        professional.UnlinkUser(now);
        var audit = ProfessionalEndpoints.CreateAudit(context, professional.Id, "PROFESSIONAL_USER_UNLINKED", now);
        audit.TargetUserId = previousUserId;
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
            return ProfessionalEndpoints.Modified();
        }
        return Results.Ok(professional.ToResponse());
    }

    private static IResult InvalidUser() => Results.BadRequest(new ApiError(
        "INVALID_PROFESSIONAL_USER", "A conta informada não pode ser vinculada ao profissional."));

    private static IResult UserAlreadyLinked() => Results.Json(new ApiError(
        "PROFESSIONAL_USER_ALREADY_LINKED", "A conta já está vinculada a outro profissional."),
        statusCode: StatusCodes.Status409Conflict);
}
