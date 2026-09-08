using System.Security.Claims;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.ProfessionalRegistrations;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Features.Customers;

namespace recepcaototem.Features.ProfessionalRegistrations;

public sealed record ProfessionalRegistrationCreateRequest(string Name, string Profession, string WhatsApp,
    string Email, string Password, string Confirmation, string? Description) : IStrictModuleRequest;
public sealed record ProfessionalRegistrationReviewRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record ProfessionalRegistrationResponse(Guid Id, string Name, string Profession, string? Description,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, string ConcurrencyToken);

public static class ProfessionalRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/professional-registration/register", Register).AllowAnonymous().AddEndpointFilter<AntiforgeryFilter>();
        endpoints.MapGet("/api/professional-registration/me", Me).RequireAuthorization("ProfessionalApplicant");
        var review = endpoints.MapGroup("/api/reception/professional-applications").RequireAuthorization("Operations");
        review.MapGet("", List);
        review.MapGet("/{id:guid}", Detail);
        review.MapPost("/{id:guid}/approve", Approve).AddEndpointFilter<AntiforgeryFilter>();
        review.MapPost("/{id:guid}/reject", Reject).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> Register(ProfessionalRegistrationCreateRequest request, HttpContext context,
        ProfessionalRegistrationRateLimiter limiter, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
        ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            request.Email, ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        var email = request.Email?.Trim() ?? "";
        if (request.Password != request.Confirmation || email.Length is < 3 or > 256 ||
            !await roles.RoleExistsAsync(SystemRoles.ProfessionalApplicant) || await users.FindByEmailAsync(email) is not null)
            return InvalidRegistration();
        ProfessionalRegistrationRequest application;
        try { application = ProfessionalRegistrationRequest.Create("pending", request.Name, request.Profession, request.WhatsApp, request.Description, time.GetUtcNow()); }
        catch (ArgumentException) { return InvalidRegistration(); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = application.Name, IsActive = true, MustChangePassword = false };
        if (!(await users.CreateAsync(user, request.Password)).Succeeded || !(await users.AddToRoleAsync(user, SystemRoles.ProfessionalApplicant)).Succeeded)
        { await transaction.RollbackAsync(ct); return InvalidRegistration(); }
        application = ProfessionalRegistrationRequest.Create(user.Id, request.Name, request.Profession, request.WhatsApp, request.Description, time.GetUtcNow());
        db.ProfessionalRegistrationRequests.Add(application);
        db.AuditEntries.Add(Audit("PROFESSIONAL_APPLICATION_CREATED", null, user.Id, application.Id, context, time.GetUtcNow()));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); return InvalidRegistration(); }
        return Results.Created("/api/professional-registration/me", ToResponse(application));
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var value = await db.ProfessionalRegistrationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationUserId == userId, ct);
        return value is null ? Results.NotFound() : Results.Ok(ToResponse(value));
    }

    private static async Task<IResult> List(string? status, int? page, int? pageSize, ApplicationDbContext db, CancellationToken ct)
    {
        var actualPage = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var query = db.ProfessionalRegistrationRequests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ProfessionalRegistrationStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        var count = await query.CountAsync(ct);
        var values = await query.OrderByDescending(x => x.CreatedAt).Skip((actualPage - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new { items = values.Select(ToResponse), page = actualPage, pageSize = size, totalCount = count });
    }
    private static async Task<IResult> Detail(Guid id, ApplicationDbContext db, CancellationToken ct)
    {
        var value = await db.ProfessionalRegistrationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return value is null ? Results.NotFound() : Results.Ok(ToResponse(value));
    }
    private static Task<IResult> Approve(Guid id, ProfessionalRegistrationReviewRequest request, HttpContext context,
        ApplicationDbContext db, UserManager<ApplicationUser> users, TimeProvider time, CancellationToken ct) => Review(id, request, true, context, db, users, time, ct);
    private static Task<IResult> Reject(Guid id, ProfessionalRegistrationReviewRequest request, HttpContext context,
        ApplicationDbContext db, UserManager<ApplicationUser> users, TimeProvider time, CancellationToken ct) => Review(id, request, false, context, db, users, time, ct);
    private static async Task<IResult> Review(Guid id, ProfessionalRegistrationReviewRequest request, bool approve,
        HttpContext context, ApplicationDbContext db, UserManager<ApplicationUser> users, TimeProvider time, CancellationToken ct)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return Results.BadRequest(new ApiError("INVALID_CONCURRENCY_TOKEN", "Token de concorrência inválido."));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var application = await db.ProfessionalRegistrationRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (application is null) return Results.NotFound();
        db.Entry(application).Property(x => x.Version).OriginalValue = version;
        if (application.Version != version) return Results.Json(new ApiError("RESOURCE_MODIFIED", "O recurso foi alterado."), statusCode: 409);
        var user = await users.FindByIdAsync(application.ApplicationUserId);
        if (user is null) return Results.Json(new ApiError("PROFESSIONAL_APPLICATION_INVALID", "A solicitação não pode ser revisada."), statusCode: 409);
        var reviewer = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!; var now = time.GetUtcNow();
        try
        {
            if (approve)
            {
                if (await db.Professionals.AnyAsync(x => x.ApplicationUserId == user.Id, ct)) return Results.Json(new ApiError("PROFESSIONAL_ALREADY_LINKED", "A conta já possui um profissional."), statusCode: 409);
                var professional = Professional.Create(application.Name, application.Profession, application.WhatsApp, now, application.Description);
                professional.LinkUser(user.Id, now); db.Professionals.Add(professional); application.Approve(reviewer, now);
                if (!(await users.AddToRoleAsync(user, SystemRoles.Profissional)).Succeeded || !(await users.RemoveFromRoleAsync(user, SystemRoles.ProfessionalApplicant)).Succeeded) throw new InvalidOperationException("Identity role transition failed.");
            }
            else application.Reject(reviewer, now);
        }
        catch (InvalidOperationException) { await transaction.RollbackAsync(ct); return Results.Json(new ApiError("PROFESSIONAL_APPLICATION_NOT_PENDING", "A solicitação já foi revisada."), statusCode: 409); }
        db.AuditEntries.Add(Audit(approve ? "PROFESSIONAL_APPLICATION_APPROVED" : "PROFESSIONAL_APPLICATION_REJECTED", reviewer, user.Id, application.Id, context, now));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return Results.Json(new ApiError("RESOURCE_MODIFIED", "O recurso foi alterado."), statusCode: 409); }
        return Results.Ok(ToResponse(application));
    }
    private static ProfessionalRegistrationResponse ToResponse(ProfessionalRegistrationRequest x) => new(x.Id, x.Name, x.Profession, x.Description, x.Status.ToString().ToUpperInvariant(), x.CreatedAt, x.ReviewedAt, ConcurrencyToken.Encode(x.Version));
    private static AuditEntry Audit(string action, string? actor, string target, Guid id, HttpContext context, DateTimeOffset now) => new() { Id = Guid.NewGuid(), Action = action, Result = "SUCCEEDED", ActorUserId = actor, TargetUserId = target, TargetEntityType = "PROFESSIONAL_APPLICATION", TargetEntityId = id, OccurredAt = now, CorrelationId = context.TraceIdentifier, IpAddress = context.Connection.RemoteIpAddress?.ToString() };
    private static IResult InvalidRegistration() => Results.BadRequest(new ApiError("INVALID_PROFESSIONAL_REGISTRATION", "Não foi possível concluir o cadastro."));
}
