using System.Data;
using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class EligibleProfessionalUserTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task All_identity_link_routes_require_administration()
    {
        await factory.ResetAsync();
        var id = Guid.NewGuid();
        var paths = new[] { "/api/admin/professionals/eligible-users", $"/api/admin/professionals/{id}/user-link" };
        foreach (var path in paths)
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync(path)).StatusCode);

        await LoginAsAsync([SystemRoles.Gerente], "eligible-manager@lumis.test");
        foreach (var path in paths)
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.PutWithCsrfAsync($"/api/admin/professionals/{id}/user-link",
            new { applicationUserId = "x", concurrencyToken = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.DeleteWithCsrfAsync($"/api/admin/professionals/{id}/user-link",
            new { concurrencyToken = "x" })).StatusCode);

        await factory.ResetAsync();
        await LoginAsAsync([SystemRoles.Profissional], "eligible-prof@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/admin/professionals/eligible-users")).StatusCode);
    }

    [Fact]
    public async Task Exact_role_matrix_active_state_and_existing_links_define_eligibility()
    {
        await factory.ResetAsync();
        await LoginAsAsync([SystemRoles.Administrador], "eligible-admin@lumis.test");
        var eligible = await factory.CreateUserAsync("eligible@lumis.test", Password, [SystemRoles.Profissional], displayName: "Ána Elegível");
        await factory.CreateUserAsync("admin-only@lumis.test", Password, [SystemRoles.Administrador], displayName: "Ana Admin");
        await factory.CreateUserAsync("manager-only@lumis.test", Password, [SystemRoles.Gerente], displayName: "Ana Manager");
        await factory.CreateUserAsync("prof-manager@lumis.test", Password, [SystemRoles.Profissional, SystemRoles.Gerente], displayName: "Ana Mixed Manager");
        await factory.CreateUserAsync("prof-admin@lumis.test", Password, [SystemRoles.Profissional, SystemRoles.Administrador], displayName: "Ana Mixed Admin");
        await factory.CreateUserAsync("inactive-prof@lumis.test", Password, [SystemRoles.Profissional], isActive: false, displayName: "Ana Inactive");
        var linked = await factory.CreateUserAsync("linked-prof@lumis.test", Password, [SystemRoles.Profissional], displayName: "Ana Linked");
        var professional = Professional.Create("Linked", "Fisio", "65999999999", factory.UtcNow);
        professional.LinkUser(linked.Id, factory.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }

        var response = await factory.Client.GetAsync("/api/admin/professionals/eligible-users?search=ana&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<EligiblePage>())!;
        Assert.Single(page.Items);
        Assert.Equal(eligible.Id, page.Items[0].UserId);
        Assert.Equal("Ána Elegível", page.Items[0].DisplayName);
        Assert.Equal("eligible@lumis.test", page.Items[0].Email);
        Assert.Equal(1, page.TotalCount);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stamp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Eligible_search_is_accent_insensitive_for_display_name_and_searches_email_with_paging()
    {
        await factory.ResetAsync();
        await LoginAsAsync([SystemRoles.Administrador], "eligible-search-admin@lumis.test");
        await factory.CreateUserAsync("named.user@lumis.test", Password, [SystemRoles.Profissional], displayName: "Bêatriz Um");
        await factory.CreateUserAsync("beatriz.two@lumis.test", Password, [SystemRoles.Profissional], displayName: "Outra Pessoa");

        var byName = (await (await factory.Client.GetAsync(
            "/api/admin/professionals/eligible-users?search=beatriz%20um&page=1&pageSize=1"))
            .Content.ReadFromJsonAsync<EligiblePage>())!;
        Assert.Single(byName.Items);
        Assert.Equal(1, byName.TotalCount);

        var byEmail = (await (await factory.Client.GetAsync(
            "/api/admin/professionals/eligible-users?search=BEATRIZ.TWO&page=1&pageSize=20"))
            .Content.ReadFromJsonAsync<EligiblePage>())!;
        Assert.Single(byEmail.Items);
        Assert.Equal("beatriz.two@lumis.test", byEmail.Items[0].Email);
    }

    [Fact]
    public async Task Approved_unaccent_extension_works_in_local_PostgreSQL()
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT upper(extensions.unaccent('Ána')) LIKE '%ANA%'";
        Assert.True(Convert.ToBoolean(await command.ExecuteScalarAsync()));
        await connection.CloseAsync();
    }

    private async Task LoginAsAsync(IReadOnlyCollection<string> roles, string email)
    {
        await factory.CreateUserAsync(email, Password, roles);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record EligiblePage(IReadOnlyList<EligibleItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record EligibleItem(string UserId, string DisplayName, string Email);
}
