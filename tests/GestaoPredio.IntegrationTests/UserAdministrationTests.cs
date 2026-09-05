using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(AuthDatabaseCollection.Name)]
public sealed class UserAdministrationTests(AuthApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => factory.ResetAsync();

    [Fact]
    public async Task Anonymous_cannot_create_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.PostWithCsrfAsync("/api/admin/users", Request())).StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Gerente)]
    [InlineData(SystemRoles.Profissional)]
    public async Task Non_administrator_cannot_create_user(string role)
    {
        await factory.CreateUserAsync("actor@lumis.test", "Valid-Password-123!", role);
        await factory.LoginAsync("actor@lumis.test", "Valid-Password-123!");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/users", Request())).StatusCode);
    }

    [Fact]
    public async Task Administrator_creates_user_with_one_time_temporary_password()
    {
        await factory.CreateUserAsync("admin@lumis.test", "Valid-Password-123!");
        await factory.LoginAsync("admin@lumis.test", "Valid-Password-123!");
        var response = await factory.PostWithCsrfAsync("/api/admin/users", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        var created = (await response.Content.ReadFromJsonAsync<CreatedUser>())!;
        Assert.False(string.IsNullOrWhiteSpace(created.TemporaryPassword));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("new.manager@lumis.test");
        Assert.NotNull(user);
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsActive);
        Assert.Equal([SystemRoles.Gerente], await users.GetRolesAsync(user));
        Assert.NotEqual(created.TemporaryPassword, user.PasswordHash);
        var audits = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.ToListAsync();
        Assert.DoesNotContain(audits, audit => System.Text.Json.JsonSerializer.Serialize(audit).Contains(created.TemporaryPassword));
    }

    [Fact]
    public async Task Invalid_role_is_rejected_without_creating_user()
    {
        await factory.CreateUserAsync("admin@lumis.test", "Valid-Password-123!");
        await factory.LoginAsync("admin@lumis.test", "Valid-Password-123!");
        var response = await factory.PostWithCsrfAsync("/api/admin/users", Request("ROOT"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("new.manager@lumis.test"));
    }

    private static object Request(string role = SystemRoles.Gerente) => new
    {
        displayName = "New Manager", email = "new.manager@lumis.test", role
    };

    private sealed record CreatedUser(string UserId, string DisplayName, string Email, string Role, string TemporaryPassword);
}
