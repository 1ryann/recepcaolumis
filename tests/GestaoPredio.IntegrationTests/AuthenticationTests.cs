using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestaoPredio.IntegrationTests;

[Collection(AuthDatabaseCollection.Name)]
public sealed class AuthenticationTests(AuthApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => factory.ResetAsync();

    [Fact]
    public void Auth_cookie_uses_approved_security_attributes()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.Equal("__Host-Lumis.Auth", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
    }

    [Fact]
    public async Task Valid_login_creates_session_and_logout_invalidates_it()
    {
        await factory.CreateUserAsync("admin@lumis.test", "Valid-Password-123!");
        var login = await factory.LoginAsync("admin@lumis.test", "Valid-Password-123!");
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Lumis.Auth="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);

        var session = await factory.Client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var logout = await factory.PostWithCsrfAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Invalid_unknown_inactive_and_locked_credentials_are_indistinguishable()
    {
        await factory.CreateUserAsync("known@lumis.test", "Valid-Password-123!");
        await factory.CreateUserAsync("inactive@lumis.test", "Valid-Password-123!", isActive: false);
        var unknown = await factory.LoginAsync("unknown@lumis.test", "Wrong-Password-123!");
        var wrong = await factory.LoginAsync("known@lumis.test", "Wrong-Password-123!");
        var inactive = await factory.LoginAsync("inactive@lumis.test", "Valid-Password-123!");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await inactive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account()
    {
        await factory.CreateUserAsync("locked@lumis.test", "Valid-Password-123!");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.LoginAsync("locked@lumis.test", "Wrong-Password-123!")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.LoginAsync("locked@lumis.test", "Valid-Password-123!")).StatusCode);
    }

    [Fact]
    public async Task Failed_login_audit_contains_no_credentials()
    {
        const string email = "missing@example.invalid";
        const string password = "Secret-value-123!";
        await factory.LoginAsync(email, password);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = db.AuditEntries.Single(x => x.Action == "LOGIN_FAILED");
        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.TargetUserId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.DoesNotContain(email, serialized);
        Assert.DoesNotContain(password, serialized);
    }

    [Fact]
    public async Task Temporary_user_must_change_password_before_administration()
    {
        await factory.CreateUserAsync("temporary@lumis.test", "Temporary-Password-123!", mustChangePassword: true);
        Assert.Equal(HttpStatusCode.NoContent,
            (await factory.LoginAsync("temporary@lumis.test", "Temporary-Password-123!")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/users", new { displayName = "Other", email = "other@lumis.test", role = SystemRoles.Gerente })).StatusCode);

        var changed = await factory.PostWithCsrfAsync("/api/auth/change-password", new
        {
            currentPassword = "Temporary-Password-123!",
            newPassword = "Replacement-Password-456!",
            confirmation = "Replacement-Password-456!"
        });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
    }
}
