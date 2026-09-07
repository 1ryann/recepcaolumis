using System.Security.Claims;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using recepcaototem.Api.Configuration;

namespace recepcaototem.Features.Auth;

public static class AuthEndpoints
{
    private static readonly ApplicationUser DummyUser = new() { DisplayName = "Dummy", UserName = "dummy" };
    private static readonly string DummyHash = new PasswordHasher<ApplicationUser>()
        .HashPassword(DummyUser, "Dummy-Password-Not-Used-123!");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.MapGet("/csrf", GetCsrf).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous().AddEndpointFilter<AntiforgeryFilter>();
        group.MapGet("/session", Session).RequireAuthorization(IdentityConfiguration.ActiveUserPolicy);
        group.MapPost("/logout", Logout).RequireAuthorization(IdentityConfiguration.ActiveUserPolicy).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization(IdentityConfiguration.ActiveUserPolicy).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static IResult GetCsrf(IAntiforgery antiforgery, HttpContext context)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new CsrfResponse(tokens.RequestToken!));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        LoginRateLimiter limiter,
        AuthAuditService audit,
        CancellationToken cancellationToken)
    {
        var rawEmail = request.Email?.Trim() ?? "";
        var normalizedEmail = users.NormalizeEmail(rawEmail);
        using var lease = await limiter.AcquireAsync(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            normalizedEmail ?? "",
            cancellationToken);
        if (!lease.IsAcquired)
        {
            await audit.WriteAsync("LOGIN_RATE_LIMITED", "REJECTED", null, null, context, cancellationToken);
            return Results.Json(new { code = "TOO_MANY_REQUESTS", message = "Muitas tentativas. Tente novamente mais tarde." }, statusCode: 429);
        }

        var user = string.IsNullOrWhiteSpace(normalizedEmail) ? null : await users.FindByEmailAsync(rawEmail);
        if (user is null)
        {
            _ = new PasswordHasher<ApplicationUser>().VerifyHashedPassword(DummyUser, DummyHash, request.Password ?? "");
            await audit.WriteAsync("LOGIN_FAILED", "REJECTED", null, null, context, cancellationToken);
            return InvalidCredentials();
        }

        if (!user.IsActive || !(await users.GetRolesAsync(user)).Any(SystemRoles.AuthenticationRoles.Contains))
        {
            _ = new PasswordHasher<ApplicationUser>().VerifyHashedPassword(DummyUser, DummyHash, request.Password ?? "");
            await audit.WriteAsync("LOGIN_FAILED", "REJECTED", null, user.Id, context, cancellationToken);
            return InvalidCredentials();
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password ?? "", false, true);
        if (!result.Succeeded)
        {
            await audit.WriteAsync("LOGIN_FAILED", "REJECTED", null, user.Id, context, cancellationToken);
            return InvalidCredentials();
        }

        await audit.WriteAsync("LOGIN_SUCCEEDED", "SUCCEEDED", user.Id, user.Id, context, cancellationToken);
        return Results.NoContent();
    }

    private static IResult Session(ClaimsPrincipal principal) => Results.Ok(new SessionResponse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)!,
        principal.FindFirstValue("lumis:display_name") ?? "",
        principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name ?? "",
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
        string.Equals(principal.FindFirstValue(IdentityConfiguration.MustChangePasswordClaim), "true", StringComparison.Ordinal)));

    private static async Task<IResult> Logout(
        ClaimsPrincipal principal,
        HttpContext context,
        SignInManager<ApplicationUser> signIn,
        AuthAuditService audit,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        await signIn.SignOutAsync();
        await audit.WriteAsync("LOGOUT_SUCCEEDED", "SUCCEEDED", userId, userId, context, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        AuthAuditService audit,
        CancellationToken cancellationToken)
    {
        if (request.NewPassword != request.Confirmation)
            return Results.BadRequest(new { code = "INVALID_PASSWORD_CHANGE", message = "Não foi possível alterar a senha." });
        var user = await users.GetUserAsync(principal);
        if (user is null || !user.IsActive)
            return Results.Unauthorized();
        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
            return Results.BadRequest(new { code = "INVALID_PASSWORD_CHANGE", message = "Não foi possível alterar a senha." });
        user.MustChangePassword = false;
        await users.UpdateSecurityStampAsync(user);
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);
        await audit.WriteAsync("PASSWORD_CHANGED", "SUCCEEDED", user.Id, user.Id, context, cancellationToken);
        return Results.NoContent();
    }

    private static IResult InvalidCredentials() => Results.Json(
        new { code = "INVALID_CREDENTIALS", message = "E-mail ou senha inválidos." }, statusCode: 401);
}
