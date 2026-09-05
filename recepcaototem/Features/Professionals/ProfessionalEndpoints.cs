using System.Security.Claims;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public static class ProfessionalEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/professionals").RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPut("/{id:guid}", Update).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/activate", Activate).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/deactivate", Deactivate).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
        string? status,
        string? search,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!PagingQuery.TryCreate(page, pageSize, status, search, out var paging, out var error))
            return Results.BadRequest(error);

        var query = db.Professionals.AsNoTracking();
        query = paging!.Status switch
        {
            PagingQuery.Active => query.Where(x => x.IsActive),
            PagingQuery.Inactive => query.Where(x => !x.IsActive),
            _ => query
        };
        if (paging.Search is not null)
            query = query.Where(x => x.NormalizedName.Contains(paging.Search) ||
                                     x.NormalizedProfession.Contains(paging.Search));

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderBy(x => x.NormalizedName)
            .ThenBy(x => x.Id)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<ProfessionalResponse>(
            entities.Select(x => x.ToResponse()).ToArray(), paging.Page, paging.PageSize, totalCount));
    }

    private static async Task<IResult> Detail(Guid id, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return professional is null ? Results.NotFound() : Results.Ok(professional.ToResponse());
    }

    private static async Task<IResult> Create(
        CreateProfessionalRequest request,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ProfessionalInput.TryValidate(request.Name, request.Profession, request.WhatsApp, out var input))
            return InvalidProfessional();

        var now = timeProvider.GetUtcNow();
        var professional = Professional.Create(input!.Name, input.Profession, input.WhatsApp, now);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Professionals.Add(professional);
        db.AuditEntries.Add(CreateAudit(context, professional.Id, "PROFESSIONAL_CREATED", now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/admin/professionals/{professional.Id}", professional.ToResponse());
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateProfessionalRequest request,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
            return InvalidToken();

        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        if (!professional.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return Modified();
        if (!ProfessionalInput.TryValidate(request.Name, request.Profession, request.WhatsApp, out var input))
            return InvalidProfessional();

        var changedFields = new List<string>(3);
        if (!string.Equals(professional.Name, input!.Name, StringComparison.Ordinal)) changedFields.Add(AuditFields.Name);
        if (!string.Equals(professional.Profession, input.Profession, StringComparison.Ordinal)) changedFields.Add(AuditFields.Profession);
        if (!string.Equals(professional.WhatsApp, input.WhatsApp, StringComparison.Ordinal)) changedFields.Add(AuditFields.WhatsApp);
        if (changedFields.Count == 0) return Results.Ok(professional.ToResponse());

        db.Entry(professional).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        professional.Update(input.Name, input.Profession, input.WhatsApp, now);
        var audit = CreateAudit(context, professional.Id, "PROFESSIONAL_UPDATED", now);
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

        return Results.Ok(professional.ToResponse());
    }

    private static Task<IResult> Activate(
        Guid id,
        ConcurrencyRequest request,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        ChangeStatus(id, request, true, context, db, timeProvider, cancellationToken);

    private static Task<IResult> Deactivate(
        Guid id,
        ConcurrencyRequest request,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        ChangeStatus(id, request, false, context, db, timeProvider, cancellationToken);

    private static async Task<IResult> ChangeStatus(
        Guid id,
        ConcurrencyRequest request,
        bool isActive,
        HttpContext context,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
            return InvalidToken();

        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        if (!professional.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return Modified();
        if (professional.IsActive == isActive) return Results.Ok(professional.ToResponse());

        db.Entry(professional).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        if (isActive) professional.Activate(now); else professional.Deactivate(now);
        db.AuditEntries.Add(CreateAudit(context, professional.Id,
            isActive ? "PROFESSIONAL_ACTIVATED" : "PROFESSIONAL_DEACTIVATED", now));

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

        return Results.Ok(professional.ToResponse());
    }

    internal static AuditEntry CreateAudit(HttpContext context, Guid targetId, string action, DateTimeOffset occurredAt)
        => new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            Action = action,
            Result = "SUCCEEDED",
            OccurredAt = occurredAt,
            CorrelationId = context.TraceIdentifier,
            TargetEntityType = "PROFESSIONAL",
            TargetEntityId = targetId
        };

    internal static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));

    internal static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);

    private static IResult InvalidProfessional() => Results.BadRequest(new ApiError(
        "INVALID_PROFESSIONAL", "Os dados do profissional são inválidos."));
}
