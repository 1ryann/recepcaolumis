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
public sealed record ResetUserPasswordResponse(string UserId, string TemporaryPassword);

public static class UserAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapUserAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/users", CreateUser)
            .RequireAuthorization("Administration")
            .AddEndpointFilter<AntiforgeryFilter>();
        endpoints.MapPost("/api/admin/users/{id}/reset-password", ResetPassword)
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

    // Administrative emergency reset: re-issues a one-time temporary password for an EXISTING
    // account and forces a change on next login. Contingency only — it does not replace the
    // future self-service / e-mail invitation flows. The temporary password lives only in
    // memory during the request and in this success response; it is never persisted, logged,
    // audited, or echoed anywhere else.
    private static async Task<IResult> ResetPassword(
        string id,
        HttpContext context,
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        ITemporaryPasswordGenerator passwords,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound(new { code = "USER_NOT_FOUND", message = "Usuário não encontrado." });

        return await ResetPasswordAsync(user, context, users, db, passwords, timeProvider, cancellationToken);
    }

    /// <summary>
    /// The reset itself, shared with the reception's own route for a customer's login
    /// (<c>CustomerAdministrationEndpoints</c>): same transaction, same rotated stamp, same audit
    /// entry. Whoever calls this has already decided that the caller may touch this account.
    /// </summary>
    internal static async Task<IResult> ResetPasswordAsync(
        ApplicationUser user,
        HttpContext context,
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        ITemporaryPasswordGenerator passwords,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var temporaryPassword = passwords.Generate();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Set the flag before the reset so the single UpdateUserAsync inside ResetPasswordAsync
        // persists it alongside the new hash and the rotated security stamp.
        user.MustChangePassword = true;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, temporaryPassword);
        if (!reset.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResetFailed();
        }
        // Rotate the security stamp explicitly so every existing session for this account is
        // invalidated at the next SecurityStampValidator check.
        var stamped = await users.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResetFailed();
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = users.GetUserId(context.User),
            TargetUserId = user.Id,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            Action = "USER_PASSWORD_RESET",
            Result = "SUCCEEDED",
            OccurredAt = timeProvider.GetUtcNow(),
            CorrelationId = context.TraceIdentifier
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new ResetUserPasswordResponse(user.Id, temporaryPassword));
    }

    private static IResult InvalidRequest() => Results.BadRequest(new
    {
        code = "INVALID_USER",
        message = "Não foi possível criar o usuário."
    });

    private static IResult ResetFailed() => Results.BadRequest(new
    {
        code = "PASSWORD_RESET_FAILED",
        message = "Não foi possível redefinir a senha."
    });
}
