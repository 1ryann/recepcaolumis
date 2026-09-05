using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalQueryTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Collection_requires_operations_policy()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.GetAsync("/api/admin/professionals")).StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "professional-query@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/admin/professionals")).StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Administrador)]
    [InlineData(SystemRoles.Gerente)]
    public async Task Administrators_and_managers_can_list_with_defaults(string role)
    {
        await factory.ResetAsync();
        await LoginAsAsync(role, $"{role.ToLowerInvariant()}-query@lumis.test");

        var response = await factory.Client.GetAsync("/api/admin/professionals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ProfessionalPage>();
        Assert.NotNull(page);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Search_status_paging_and_ordering_are_executed_by_the_api()
    {
        await factory.ResetAsync();
        await SeedAsync(
            Professional.Create("Ána Zeta", "Fisióterapia", "65999999991", DateTimeOffset.UtcNow),
            Professional.Create("ana Alfa", "FISIOTERAPIA", "65999999992", DateTimeOffset.UtcNow),
            Professional.Create("Bruno", "Odontologia", "65999999993", DateTimeOffset.UtcNow));
        var inactive = Professional.Create("Ana Inativa", "Fisioterapia", "65999999994", DateTimeOffset.UtcNow);
        inactive.Deactivate(DateTimeOffset.UtcNow);
        await SeedAsync(inactive);
        await LoginAsAsync(SystemRoles.Administrador, "admin-search@lumis.test");

        var response = await factory.Client.GetAsync(
            "/api/admin/professionals?search=%20%20fisioterapia%20&status=active&page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<ProfessionalPage>())!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["ana Alfa", "Ána Zeta"], page.Items.Select(x => x.Name));
        Assert.All(page.Items, item => Assert.True(item.IsActive));

        var secondPage = (await (await factory.Client.GetAsync(
            "/api/admin/professionals?search=%C3%A1na&status=all&page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<ProfessionalPage>())!;
        Assert.Single(secondPage.Items);
        Assert.Equal("Ána Zeta", secondPage.Items[0].Name);
        Assert.Equal(3, secondPage.TotalCount);

        var beyond = (await (await factory.Client.GetAsync(
            "/api/admin/professionals?search=ana&status=all&page=999&pageSize=2"))
            .Content.ReadFromJsonAsync<ProfessionalPage>())!;
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);
    }

    [Theory]
    [InlineData("page=0", "INVALID_PAGE")]
    [InlineData("pageSize=101", "INVALID_PAGE_SIZE")]
    [InlineData("status=deleted", "INVALID_STATUS")]
    [InlineData("search=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "INVALID_SEARCH")]
    public async Task Invalid_query_is_rejected_with_stable_code(string query, string code)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, $"manager-{Guid.NewGuid():N}@lumis.test");

        var response = await factory.Client.GetAsync($"/api/admin/professionals?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Detail_returns_approved_projection_only_and_missing_is_not_found()
    {
        await factory.ResetAsync();
        var professional = Professional.Create("Ana", "Psicóloga", "(65) 99999-9999", DateTimeOffset.UtcNow);
        await SeedAsync(professional);
        await LoginAsAsync(SystemRoles.Administrador, "admin-detail@lumis.test");

        var response = await factory.Client.GetAsync($"/api/admin/professionals/{professional.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"concurrencyToken\"", json);
        Assert.Contains("\"hasLinkedUser\"", json);
        Assert.DoesNotContain("normalizedName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photoFileId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("applicationUserId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/admin/professionals/{Guid.NewGuid()}")).StatusCode);
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private async Task SeedAsync(params Professional[] professionals)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.AddRange(professionals);
        await db.SaveChangesAsync();
    }

    private sealed record ProfessionalPage(IReadOnlyList<ProfessionalItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record ProfessionalItem(Guid Id, string Name, bool IsActive, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}
