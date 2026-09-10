using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// <c>POST /api/customer/booking-handoffs/resolve</c> (Task 13): a <see cref="IdentityConfiguration"/>
/// CustomerPolicy read-only endpoint that returns just the professional context of a handoff so the
/// phone's booking screen can pre-select the professional. It must never mutate the handoff and must
/// never echo customer data, reservation data, or the token.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class CustomerHandoffApiTests(ModulesApiFactory factory)
{
    private const string Path = "/api/customer/booking-handoffs/resolve";

    [Fact]
    public async Task Resolve_returns_professional_context_for_an_authenticated_customer()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadFromJsonAsync<ResolveBody>();
        Assert.Equal(b.Id, body!.HandoffId);
        Assert.Equal(prof, body.ProfessionalId);
        Assert.Equal("Dra. Ana", body.ProfessionalName);
        Assert.Equal("Fisioterapia", body.Profession);
        Assert.Equal(b.ExpiresAt, body.ExpiresAt);

        // No customer data, no reservation data, no token echoed back.
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain(b.HandoffToken, raw);
        Assert.DoesNotContain(b.StatusToken, raw);
        Assert.DoesNotContain(seed.CustomerName, raw);
        Assert.DoesNotContain(seed.CustomerPhone, raw);
        Assert.DoesNotContain("reservation", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_on_an_expired_handoff_is_410()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(DateTimeOffset.UtcNow);
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        factory.FreezeTime(factory.UtcNow.AddMinutes(6));   // past the initial 5-minute window
        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Gone, r.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await r.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Resolve_on_a_non_pending_handoff_is_410()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);

        // Cancel folds the row to Expired while it is still inside the time window.
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Gone, r.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await r.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Resolve_with_a_bad_token_is_400()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await r.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Resolve_requires_an_authenticated_customer()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);

        // No login: CustomerPolicy on the group rejects the anonymous caller before the handler runs.
        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Resolve_never_mutates_the_handoff()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var (startedBefore, expiresBefore, statusBefore) = await ReadHandoffAsync(b.Id);
        Assert.Null(startedBefore);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken })).StatusCode);

        var (startedAfter, expiresAfter, statusAfter) = await ReadHandoffAsync(b.Id);
        Assert.Null(startedAfter);
        Assert.Equal(startedBefore, startedAfter);
        Assert.Equal(expiresBefore, expiresAfter);
        Assert.Equal(statusBefore, statusAfter);
    }

    private async Task<(DateTimeOffset? StartedAt, DateTimeOffset ExpiresAt, TotemBookingHandoffStatus Status)> ReadHandoffAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync(x => x.Id == id);
        return (row.StartedAt, row.ExpiresAt, row.Status);
    }

    private async Task<CustomerSeed> SeedCustomerAsync()
    {
        const string password = "Valid-Password-123!";
        const string name = "Cliente Handoff";
        const string phone = "+5569988887777";
        var user = await factory.CreateUserAsync($"handoff-customer-{Guid.NewGuid():N}@lumis.test", password,
            [SystemRoles.Customer], displayName: name);
        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(name, phone, now);
        customer.LinkUser(user.Id, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return new CustomerSeed(user.Email!, password, name, phone);
    }

    private async Task<CreateBody> CreateHandoffAsync(Guid professionalId)
    {
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<CreateBody>())!;
    }

    private sealed record CustomerSeed(string Email, string Password, string CustomerName, string CustomerPhone);
    private sealed record CreateBody(Guid Id, string HandoffToken, string StatusToken, DateTimeOffset ExpiresAt, string ProfessionalName, string Profession);
    private sealed record ResolveBody(Guid HandoffId, Guid ProfessionalId, string ProfessionalName, string Profession, DateTimeOffset ExpiresAt);
    private sealed record ErrorBody(string Code, string Message);
}
