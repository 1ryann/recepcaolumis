using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TenantApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Tenant_endpoints_require_operations_and_mutations_require_csrf()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/admin/tenants")).StatusCode);
        await LoginAsync(SystemRoles.Profissional);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/admin/tenants")).StatusCode);
        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/admin/tenants",
            new { name = "Ana", kind = "INDIVIDUAL" })).StatusCode);
    }

    [Fact]
    public async Task Create_list_search_update_and_status_use_real_postgresql_data()
    {
        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Administrador);
        var createdResponse = await factory.PostWithCsrfAsync("/api/admin/tenants",
            new { name = "Clínica São José", kind = "LEGAL_ENTITY" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<TenantPayload>())!;
        Assert.True(created.IsActive);
        Assert.NotEmpty(created.ConcurrencyToken);

        var page = (await (await factory.Client.GetAsync("/api/admin/tenants?search=clinica&status=active"))
            .Content.ReadFromJsonAsync<TenantPage>())!;
        Assert.Single(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);

        var updatedResponse = await factory.PutWithCsrfAsync($"/api/admin/tenants/{created.Id}", new
        {
            name = "Clínica Ágil", kind = "LEGAL_ENTITY", concurrencyToken = created.ConcurrencyToken
        });
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<TenantPayload>())!;
        Assert.Equal("Clínica Ágil", updated.Name);
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);

        var disabledResponse = await factory.PostWithCsrfAsync($"/api/admin/tenants/{created.Id}/deactivate",
            new { concurrencyToken = updated.ConcurrencyToken });
        var disabled = (await disabledResponse.Content.ReadFromJsonAsync<TenantPayload>())!;
        Assert.False(disabled.IsActive);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.GetAsync($"/api/admin/tenants/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_contract_extra_json_and_stale_token_are_rejected()
    {
        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/tenants",
            new { name = "Ana", kind = "UNKNOWN" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/tenants",
            new { name = "Ana", kind = "INDIVIDUAL", isActive = false })).StatusCode);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/tenants",
            new { name = "Ana", kind = "INDIVIDUAL" })).Content.ReadFromJsonAsync<TenantPayload>())!;
        var first = await factory.PutWithCsrfAsync($"/api/admin/tenants/{created.Id}", new
            { name = "Ana 2", kind = "INDIVIDUAL", concurrencyToken = created.ConcurrencyToken });
        first.EnsureSuccessStatusCode();
        var stale = await factory.PutWithCsrfAsync($"/api/admin/tenants/{created.Id}", new
            { name = "Ana 3", kind = "INDIVIDUAL", concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    private async Task LoginAsync(string role)
    {
        var email = $"tenant-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record TenantPage(IReadOnlyList<TenantPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record TenantPayload(Guid Id, string Name, string Kind, bool IsActive, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}
