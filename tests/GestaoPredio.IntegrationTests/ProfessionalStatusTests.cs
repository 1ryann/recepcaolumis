using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalStatusTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Status_routes_require_operations_and_antiforgery()
    {
        await factory.ResetAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.PostAsJsonAsync($"/api/admin/professionals/{id}/deactivate", new { concurrencyToken = "x" })).StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "professional-status@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync($"/api/admin/professionals/{id}/deactivate", new { concurrencyToken = "x" })).StatusCode);

        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "manager-status-csrf@lumis.test");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync($"/api/admin/professionals/{id}/deactivate", new { concurrencyToken = "x" })).StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Administrador)]
    [InlineData(SystemRoles.Gerente)]
    public async Task Operations_roles_can_deactivate_and_activate(string role)
    {
        await factory.ResetAsync();
        await LoginAsAsync(role, $"status-{role.ToLowerInvariant()}@lumis.test");
        var created = await CreateAsync();
        factory.AdvanceTime(TimeSpan.FromSeconds(1));

        var deactivatedResponse = await factory.PostWithCsrfAsync(
            $"/api/admin/professionals/{created.Id}/deactivate", new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, deactivatedResponse.StatusCode);
        var deactivated = (await deactivatedResponse.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.False(deactivated.IsActive);
        Assert.NotEqual(created.ConcurrencyToken, deactivated.ConcurrencyToken);
        Assert.True(deactivated.UpdatedAt > created.UpdatedAt);

        var activatedResponse = await factory.PostWithCsrfAsync(
            $"/api/admin/professionals/{created.Id}/activate", new { concurrencyToken = deactivated.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, activatedResponse.StatusCode);
        var activated = (await activatedResponse.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.True(activated.IsActive);
        Assert.NotEqual(deactivated.ConcurrencyToken, activated.ConcurrencyToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_DEACTIVATED"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_ACTIVATED"));
    }

    [Fact]
    public async Task Current_same_state_is_noop_and_stale_same_state_is_conflict()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-status-noop@lumis.test");
        var created = await CreateAsync();

        var noop = await factory.PostWithCsrfAsync($"/api/admin/professionals/{created.Id}/activate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        var unchanged = (await noop.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.Equal(created.ConcurrencyToken, unchanged.ConcurrencyToken);
        Assert.Equal(created.UpdatedAt, unchanged.UpdatedAt);

        var changed = await factory.PostWithCsrfAsync($"/api/admin/professionals/{created.Id}/deactivate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var stale = await factory.PostWithCsrfAsync($"/api/admin/professionals/{created.Id}/deactivate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_DEACTIVATED"));
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_ACTIVATED"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("AQID")]
    public async Task Status_rejects_missing_or_invalid_token(string? token)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, $"manager-status-token-{Guid.NewGuid():N}@lumis.test");
        var created = await CreateAsync();

        var response = await factory.PostWithCsrfAsync($"/api/admin/professionals/{created.Id}/deactivate",
            new { concurrencyToken = token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CONCURRENCY_TOKEN", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Deactivation_does_not_change_linked_identity_user()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-status-link@lumis.test");
        var linkedUser = await factory.CreateUserAsync("linked-status@lumis.test", Password, [SystemRoles.Profissional]);
        var created = await CreateAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var professional = await db.Professionals.SingleAsync(x => x.Id == created.Id);
            professional.LinkUser(linkedUser.Id, factory.UtcNow);
            await db.SaveChangesAsync();
        }
        var current = await GetAsync(created.Id);

        var response = await factory.PostWithCsrfAsync($"/api/admin/professionals/{created.Id}/deactivate",
            new { concurrencyToken = current.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var users = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var unchangedUser = (await users.FindByIdAsync(linkedUser.Id))!;
        Assert.True(unchangedUser.IsActive);
        Assert.Equal([SystemRoles.Profissional], await users.GetRolesAsync(unchangedUser));
    }

    private async Task<ProfessionalPayload> CreateAsync()
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = "Ana", profession = "Fisio", whatsApp = "65999999999"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
    }

    private async Task<ProfessionalPayload> GetAsync(Guid id) =>
        (await (await factory.Client.GetAsync($"/api/admin/professionals/{id}"))
            .Content.ReadFromJsonAsync<ProfessionalPayload>())!;

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record ProfessionalPayload(Guid Id, bool IsActive, DateTimeOffset UpdatedAt, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}
