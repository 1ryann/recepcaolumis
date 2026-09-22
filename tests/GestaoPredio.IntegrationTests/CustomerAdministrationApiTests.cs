using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

// Deactivating a customer had no screen and no API: the only way was an UPDATE against the production database.
[Collection(ModulesDatabaseCollection.Name)]
public sealed class CustomerAdministrationApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Reception_deactivates_and_reactivates_a_customer_and_both_are_audited()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "reception-customers@lumis.test");
        var customer = await SeedAsync("Maria Clara Souza", "69999990101");

        var listed = await ListAsync();
        var row = Assert.Single(listed.Items);
        Assert.True(row.IsActive);
        Assert.Equal("+5569999990101", row.Phone);

        var deactivated = await PostAsync(customer.Id, "deactivate", row.ConcurrencyToken);
        Assert.False(deactivated.IsActive);

        var reactivated = await PostAsync(customer.Id, "activate", deactivated.ConcurrencyToken);
        Assert.True(reactivated.IsActive);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "CUSTOMER_DEACTIVATED" && x.TargetEntityType == "CUSTOMER"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "CUSTOMER_ACTIVATED" && x.TargetEntityType == "CUSTOMER"));
    }

    [Fact]
    public async Task A_stale_token_loses_and_the_same_state_changes_nothing()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-customers-token@lumis.test");
        var customer = await SeedAsync("João Pedro", "69999990102");
        var row = Assert.Single((await ListAsync()).Items);

        var noop = await PostAsync(customer.Id, "activate", row.ConcurrencyToken);
        Assert.Equal(row.ConcurrencyToken, noop.ConcurrencyToken);

        Assert.False((await PostAsync(customer.Id, "deactivate", row.ConcurrencyToken)).IsActive);
        var stale = await factory.PostWithCsrfAsync($"/api/admin/customers/{customer.Id}/deactivate",
            new { concurrencyToken = row.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "CUSTOMER_DEACTIVATED"));
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "CUSTOMER_ACTIVATED"));
    }

    [Fact]
    public async Task The_list_filters_by_status_and_finds_a_customer_by_name_or_typed_phone()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-customers-search@lumis.test");
        var maria = await SeedAsync("Maria Clara Souza", "69999990103");
        var joao = await SeedAsync("João Pedro Lima", "69999990104");
        var row = (await ListAsync()).Items.Single(x => x.Id == joao.Id);
        await PostAsync(joao.Id, "deactivate", row.ConcurrencyToken);

        Assert.Equal([maria.Id], (await ListAsync("status=active")).Items.Select(x => x.Id));
        Assert.Equal([joao.Id], (await ListAsync("status=inactive")).Items.Select(x => x.Id));
        Assert.Equal([maria.Id], (await ListAsync("search=clara")).Items.Select(x => x.Id));
        // The number as reception has it in hand, without the +55 the record stores.
        Assert.Equal([joao.Id], (await ListAsync("search=69999990104")).Items.Select(x => x.Id));
        Assert.Empty((await ListAsync("search=69999990999")).Items);
    }

    [Fact]
    public async Task The_routes_need_operations_and_antiforgery()
    {
        await factory.ResetAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/admin/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.PostAsJsonAsync($"/api/admin/customers/{id}/deactivate", new { concurrencyToken = "x" })).StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "professional-customers@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/admin/customers")).StatusCode);

        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "manager-customers-csrf@lumis.test");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync($"/api/admin/customers/{id}/deactivate", new { concurrencyToken = "x" })).StatusCode);
    }

    private async Task<Customer> SeedAsync(string name, string phone)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customer = Customer.Create(name, phone, factory.UtcNow);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, factory.UtcNow);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private async Task<PagePayload> ListAsync(string? query = null) =>
        (await (await factory.Client.GetAsync($"/api/admin/customers{(query is null ? "" : "?" + query)}"))
            .Content.ReadFromJsonAsync<PagePayload>())!;

    private async Task<CustomerPayload> PostAsync(Guid id, string action, string token)
    {
        var response = await factory.PostWithCsrfAsync($"/api/admin/customers/{id}/{action}", new { concurrencyToken = token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerPayload>())!;
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record CustomerPayload(Guid Id, string Name, string Phone, bool IsActive, bool HasAccount, string ConcurrencyToken);
    private sealed record PagePayload(IReadOnlyList<CustomerPayload> Items, int Page, int PageSize, int TotalCount);
}
