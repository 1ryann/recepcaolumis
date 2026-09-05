using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;

namespace recepcaototem.Features.Users;

public sealed record CreateUserRequest(string DisplayName, string Email, string Role);
public sealed record CreatedUserResponse(string UserId, string DisplayName, string Email, string Role, string TemporaryPassword);

public static class UserAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapUserAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/users", CreateUser)
            .RequireAuthorization("Administration")
            .AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        HttpContext context,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole> roles,
        ApplicationDbContext db,
        ITemporaryPasswordGenerator passwords,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        if (displayName.Length is < 1 or > 200 || email.Length is < 3 or > 256 || !SystemRoles.All.Contains(request.Role))
            return InvalidRequest();
        if (!await roles.RoleExistsAsync(request.Role))
            return Results.Json(new { code = "IDENTITY_NOT_PROVISIONED", message = "Configuração de identidade indisponível." }, statusCode: 503);

        var temporaryPassword = passwords.Generate();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            MustChangePassword = true
        };
        var created = await users.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidRequest();
        }
        var assigned = await users.AddToRoleAsync(user, request.Role);
        if (!assigned.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidRequest();
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = users.GetUserId(context.User),
            TargetUserId = user.Id,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            Action = "USER_CREATED",
            Result = "SUCCEEDED",
            OccurredAt = timeProvider.GetUtcNow(),
            CorrelationId = context.TraceIdentifier
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/admin/users/{user.Id}",
            new CreatedUserResponse(user.Id, user.DisplayName, user.Email!, request.Role, temporaryPassword));
    }

    private static IResult InvalidRequest() => Results.BadRequest(new
    {
        code = "INVALID_USER",
        message = "Não foi possível criar o usuário."
    });
}
