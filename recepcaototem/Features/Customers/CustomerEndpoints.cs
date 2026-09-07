using System.Security.Claims;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Api.Configuration;

namespace recepcaototem.Features.Customers;

public sealed record CustomerRegisterRequest(string Name, string Phone, string Email, string Password, string Confirmation) : IStrictModuleRequest;
public sealed record CustomerProfileResponse(Guid Id, string Name, string Phone, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/customer/register", Register).AllowAnonymous().AddEndpointFilter<AntiforgeryFilter>();
        endpoints.MapGet("/api/customer/me", Me).RequireAuthorization(IdentityConfiguration.CustomerPolicy);
        return endpoints;
    }

    private static async Task<IResult> Register(CustomerRegisterRequest request, HttpContext context, CustomerPublicRateLimiter limiter,
        UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        using var rateLease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", request.Email ?? string.Empty, cancellationToken);
        if (!rateLease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        if (request.Password != request.Confirmation || name.Length is < 1 or > Customer.MaximumNameLength ||
            email.Length is < 3 or > 256 || !await roles.RoleExistsAsync(SystemRoles.Customer))
            return InvalidRegistration();
        if (await users.FindByEmailAsync(email) is not null ||
            !GestaoPredio.Domain.Professionals.WhatsAppNormalizer.TryNormalize(request.Phone, out var phone))
            return InvalidRegistration();
        if (await db.Customers.AnyAsync(x => x.NormalizedPhone == phone, cancellationToken))
            return InvalidRegistration();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = name, IsActive = true, MustChangePassword = false };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) { await transaction.RollbackAsync(cancellationToken); return InvalidRegistration(); }
        var assigned = await users.AddToRoleAsync(user, SystemRoles.Customer);
        if (!assigned.Succeeded) { await transaction.RollbackAsync(cancellationToken); return InvalidRegistration(); }

        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(name, phone, now);
        customer.LinkUser(user.Id, now);
        db.Customers.Add(customer);
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry
        {
            Id = Guid.NewGuid(), Action = "CUSTOMER_CREATED", Result = "SUCCEEDED", TargetUserId = user.Id,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(), OccurredAt = now, CorrelationId = context.TraceIdentifier
        });
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry
        {
            Id = Guid.NewGuid(), Action = "CUSTOMER_ACCOUNT_LINKED", Result = "SUCCEEDED", TargetUserId = user.Id,
            TargetEntityType = "CUSTOMER", TargetEntityId = customer.Id, IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = now, CorrelationId = context.TraceIdentifier
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidRegistration();
        }
        return Results.Created("/api/customer/me", new CustomerProfileResponse(customer.Id, customer.Name, customer.Phone, customer.IsActive, customer.CreatedAt, customer.UpdatedAt));
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.NotFound();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationUserId == userId, cancellationToken);
        return customer is null ? Results.NotFound() : Results.Ok(new CustomerProfileResponse(customer.Id, customer.Name, customer.Phone, customer.IsActive, customer.CreatedAt, customer.UpdatedAt));
    }

    private static IResult InvalidRegistration() => Results.BadRequest(new { code = "INVALID_CUSTOMER_REGISTRATION", message = "Não foi possível concluir o cadastro." });
}
