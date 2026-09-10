using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemBookingHandoffApiTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Create_returns_two_distinct_tokens_stored_only_as_hashes()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
        var log = factory.CaptureLogs();
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = prof });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await res.Content.ReadFromJsonAsync<CreateBody>();
        Assert.NotEqual(b!.HandoffToken, b.StatusToken);
        Assert.Equal("Dra. Ana", b.ProfessionalName);
        Assert.Equal("Fisioterapia", b.Profession);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TotemBookingHandoffs.SingleAsync();
        Assert.Equal(32, row.HandoffTokenHash.Length);
        Assert.Equal(32, row.StatusTokenHash.Length);
        Assert.NotEqual(row.HandoffTokenHash, row.StatusTokenHash);
        Assert.DoesNotContain(b.HandoffToken, log.Text);
        Assert.DoesNotContain(b.StatusToken, log.Text);
    }

    [Fact]
    public async Task Status_is_PENDING_then_EXPIRED_after_the_window_and_wrong_token_is_generic()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(DateTimeOffset.UtcNow);
        var b = await CreateHandoffAsync(prof);
        var p = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("PENDING", (await p.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        var wrong = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await wrong.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        factory.FreezeTime(factory.UtcNow.AddMinutes(6));
        var e = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("EXPIRED", (await e.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TotemBookingHandoffs.SingleAsync();
        Assert.Equal(GestaoPredio.Domain.Customers.TotemBookingHandoffStatus.Expired, row.Status);
    }

    [Fact]
    public async Task Status_unknown_id_is_the_same_generic_400_as_a_wrong_token()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);
        var unknown = await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{Guid.NewGuid()}/status", new { statusToken = b.StatusToken });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await unknown.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Cancel_expires_a_pending_handoff_and_is_idempotent()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
        var s = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("EXPIRED", (await s.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditEntries.Where(x => x.Action == "TOTEM_HANDOFF_CANCELLED").ToListAsync();
        Assert.Single(audits);
    }

    [Fact]
    public async Task Cancel_requires_the_matching_status_token_and_never_reveals_existence()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var a = await CreateHandoffAsync(prof);
        var other = await CreateHandoffAsync(prof);

        // handoff A's id + handoff B's statusToken -> generic 400, A is NOT cancelled
        var cross = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/cancel", new { statusToken = other.StatusToken });
        Assert.Equal(HttpStatusCode.BadRequest, cross.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await cross.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // garbage token for a real id, and any token for a random id -> same generic 400 (no oracle)
        var garbage = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/cancel", new { statusToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        var unknown = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{Guid.NewGuid()}/cancel", new { statusToken = a.StatusToken });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // A is still PENDING (no mutation happened on any failed attempt)
        var still = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/status", new { statusToken = a.StatusToken });
        Assert.Equal("PENDING", (await still.Content.ReadFromJsonAsync<StatusBody>())!.Status);
    }

    [Fact]
    public async Task Create_rejects_an_inactive_or_unknown_professional_generically()
    {
        await factory.ResetAsync();
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    private async Task<CreateBody> CreateHandoffAsync(Guid professionalId)
    {
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<CreateBody>())!;
    }

    private sealed record CreateBody(Guid Id, string HandoffToken, string StatusToken, DateTimeOffset ExpiresAt, string ProfessionalName, string Profession);
    private sealed record StatusBody(string Status, DateTimeOffset? ExpiresAt, string? ProfessionalName, DateTimeOffset? StartAt, string? RoomName);
    private sealed record ErrorBody(string Code, string Message);
}
