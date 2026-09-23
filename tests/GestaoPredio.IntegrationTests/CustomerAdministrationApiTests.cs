using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
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

    // Production (2026-09-23): a deactivated customer still signed in — Customer.IsActive and
    // ApplicationUser.IsActive are separate flags and the reception screen only flipped the first —
    // so the area loaded (GET /me does not filter) while every scheduling route answered a bare 404.
    [Fact]
    public async Task Deactivating_a_customer_closes_the_login_of_the_linked_account()
    {
        await factory.ResetAsync();
        var customerEmail = $"customer-access-{Guid.NewGuid():N}@lumis.test";
        var account = await factory.CreateUserAsync(customerEmail, Password, [SystemRoles.Customer], displayName: "Cliente Teste");
        await LoginAsAsync(SystemRoles.Gerente, "reception-customer-access@lumis.test");
        var customer = await SeedAsync("Cliente Teste", "69999990105", account.Id);
        var row = (await ListAsync()).Items.Single(x => x.Id == customer.Id);

        var stampBefore = await ReadAccountAsync(account.Id);
        var deactivated = await PostAsync(customer.Id, "deactivate", row.ConcurrencyToken);
        Assert.False(deactivated.IsActive);

        var stored = await ReadAccountAsync(account.Id);
        Assert.False(stored.IsActive);
        // A cookie already in the browser is retired by the security stamp validator, which the
        // application cookie is wired to — both halves are asserted, not assumed.
        Assert.NotEqual(stampBefore.SecurityStamp, stored.SecurityStamp);
        Assert.NotNull(factory.Services.GetService<ISecurityStampValidator>());

        // Sign the manager out first: otherwise the cookie under test is still theirs.
        await factory.PostWithCsrfAsync("/api/auth/logout", new { });
        var blocked = await factory.LoginAsync(customerEmail, Password);
        // Generic on purpose: AuthenticationTests keeps unknown, wrong, inactive and locked
        // credentials indistinguishable, so the login must not name the reason here.
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Contains("INVALID_CREDENTIALS", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/customer/me")).StatusCode);

        await LoginAsAsync(SystemRoles.Gerente, "reception-customer-access-2@lumis.test");
        await PostAsync(customer.Id, "activate", deactivated.ConcurrencyToken);
        await factory.PostWithCsrfAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(customerEmail, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.GetAsync("/api/customer/me")).StatusCode);
    }
    // Reception asked to remove a customer for good. A record nothing points at goes away entirely;
    // one with reservations, visits or WhatsApp notices keeps the rows and loses the person.
    [Fact]
    public async Task Deleting_a_customer_without_history_removes_the_record_and_its_login()
    {
        await factory.ResetAsync();
        var email = $"customer-delete-{Guid.NewGuid():N}@lumis.test";
        var account = await factory.CreateUserAsync(email, Password, [SystemRoles.Customer], displayName: "Cliente Some");
        await LoginAsAsync(SystemRoles.Gerente, "reception-customer-delete@lumis.test");
        var customer = await SeedAsync("Cliente Some", "69999990106", account.Id);
        var row = (await ListAsync()).Items.Single(x => x.Id == customer.Id);

        var response = await factory.DeleteWithCsrfAsync($"/api/admin/customers/{customer.Id}", new { concurrencyToken = row.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DELETED", (await response.Content.ReadFromJsonAsync<DeletePayload>())!.Outcome);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Customers.SingleOrDefaultAsync(x => x.Id == customer.Id));
        Assert.Null(await ReadAccountAsync(account.Id));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "CUSTOMER_DELETED" && x.TargetEntityType == "CUSTOMER"));
    }

    [Fact]
    public async Task Deleting_a_customer_with_history_anonymizes_it_and_keeps_the_reservation()
    {
        await factory.ResetAsync();
        var email = $"customer-anon-{Guid.NewGuid():N}@lumis.test";
        var account = await factory.CreateUserAsync(email, Password, [SystemRoles.Customer], displayName: "Cliente Histórico");
        await LoginAsAsync(SystemRoles.Gerente, "reception-customer-anon@lumis.test");
        var customer = await SeedAsync("Cliente Histórico", "69999990107", account.Id);
        var reservationId = await SeedReservationAsync(customer.Id);
        var row = (await ListAsync()).Items.Single(x => x.Id == customer.Id);

        var response = await factory.DeleteWithCsrfAsync($"/api/admin/customers/{customer.Id}", new { concurrencyToken = row.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ANONYMIZED", (await response.Content.ReadFromJsonAsync<DeletePayload>())!.Outcome);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Customers.SingleAsync(x => x.Id == customer.Id);
        Assert.Equal("Cliente excluído", stored.Name);
        Assert.DoesNotContain("99999", stored.Phone);
        Assert.Null(stored.ApplicationUserId);
        Assert.False(stored.IsActive);
        Assert.Null(await ReadAccountAsync(account.Id));
        // The appointment history survives, still pointing at the (now nameless) customer.
        Assert.True(await db.Reservations.AnyAsync(x => x.Id == reservationId && x.CustomerId == customer.Id));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "CUSTOMER_ANONYMIZED" && x.TargetEntityType == "CUSTOMER"));
    }

    [Fact]
    public async Task Deleting_needs_a_fresh_token_an_existing_customer_operations_and_antiforgery()
    {
        await factory.ResetAsync();
        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.DeleteWithCsrfAsync($"/api/admin/customers/{missing}", new { concurrencyToken = "x" })).StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "professional-customer-delete@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.DeleteWithCsrfAsync($"/api/admin/customers/{missing}", new { concurrencyToken = "x" })).StatusCode);

        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "reception-customer-delete-token@lumis.test");
        var customer = await SeedAsync("Cliente Token", "69999990108");
        var row = (await ListAsync()).Items.Single(x => x.Id == customer.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.DeleteWithCsrfAsync($"/api/admin/customers/{missing}", new { concurrencyToken = row.ConcurrencyToken })).StatusCode);

        await PostAsync(customer.Id, "deactivate", row.ConcurrencyToken);
        // The token the screen was holding is now stale, so the delete must lose instead of guessing.
        Assert.Equal(HttpStatusCode.Conflict,
            (await factory.DeleteWithCsrfAsync($"/api/admin/customers/{customer.Id}", new { concurrencyToken = row.ConcurrencyToken })).StatusCode);

        using var noCsrf = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customer.Id}")
        {
            Content = JsonContent.Create(new { concurrencyToken = row.ConcurrencyToken })
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.SendAsync(noCsrf)).StatusCode);
    }

    // The same login can belong to a professional as well (Professionals.ApplicationUserId has its own
    // foreign key with no cascade). Deleting it would take their access away and break the constraint.
    [Fact]
    public async Task Deleting_a_customer_keeps_a_login_a_professional_still_uses()
    {
        await factory.ResetAsync();
        var email = $"customer-shared-{Guid.NewGuid():N}@lumis.test";
        var account = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional], displayName: "Pessoa Dupla");
        await LoginAsAsync(SystemRoles.Gerente, "reception-customer-shared@lumis.test");
        var customer = await SeedAsync("Pessoa Dupla", "69999990109", account.Id);
        await SeedProfessionalWithAccountAsync(account.Id);
        var row = (await ListAsync()).Items.Single(x => x.Id == customer.Id);

        var response = await factory.DeleteWithCsrfAsync($"/api/admin/customers/{customer.Id}", new { concurrencyToken = row.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Customers.SingleOrDefaultAsync(x => x.Id == customer.Id));
        // The professional keeps working: their login survived the customer being removed.
        Assert.NotNull(await ReadAccountAsync(account.Id));
        Assert.True(await db.Professionals.AnyAsync(x => x.ApplicationUserId == account.Id));
    }

    private async Task SeedProfessionalWithAccountAsync(string accountId)
    {
        var now = factory.UtcNow;
        var professional = Professional.Create("Pessoa Dupla", "Fisioterapia",
            $"659{Random.Shared.Next(10000000, 99999999)}", now);
        professional.LinkUser(accountId, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedReservationAsync(Guid customerId)
    {
        var now = factory.UtcNow;
        var room = Room.Create($"Sala {Guid.NewGuid():N}"[..20], null, 10, 50, now);
        var professional = Professional.Create("Profissional Histórico", "Fisioterapia",
            $"659{Random.Shared.Next(10000000, 99999999)}", now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id,
            now.AddDays(-2), now.AddDays(-2).AddHours(1), "seed-user", now, customerId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, professional, reservation);
        await db.SaveChangesAsync();
        return reservation.Id;
    }

    private async Task<ApplicationUser> ReadAccountAsync(string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByIdAsync(id))!;
    }

    private async Task<Customer> SeedAsync(string name, string phone, string? applicationUserId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customer = Customer.Create(name, phone, factory.UtcNow);
        if (applicationUserId is not null) customer.LinkUser(applicationUserId, factory.UtcNow);
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

    private sealed record DeletePayload(string Outcome);
    private sealed record CustomerPayload(Guid Id, string Name, string Phone, bool IsActive, bool HasAccount, string ConcurrencyToken);
    private sealed record PagePayload(IReadOnlyList<CustomerPayload> Items, int Page, int PageSize, int TotalCount);
}
