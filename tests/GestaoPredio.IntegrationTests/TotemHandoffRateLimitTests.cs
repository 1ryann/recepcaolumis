using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemHandoffRateLimitTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Status_polling_never_429s_within_its_own_generous_budget()
    {
        await factory.ResetAsync();
        using var host = factory.WithConfig(
            ("RateLimiting:HandoffWindowSeconds", "600"),
            ("RateLimiting:HandoffStatusPermitLimit", "50"),
            ("RateLimiting:HandoffCreateIpPermitLimit", "50"));
        var prof = await factory.SeedActiveProfessionalAsync();
        var create = await host.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = prof });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await create.Content.ReadFromJsonAsync<CreateBody>();
        for (var i = 0; i < 45; i++)   // ~90 s of 2 s polling
        {
            var r = await host.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{body!.Id}/status", new { statusToken = body.StatusToken });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
        }
        var r51 = await host.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{body!.Id}/status", new { statusToken = body.StatusToken });
        Assert.Equal(HttpStatusCode.TooManyRequests, r51.StatusCode);   // 46th..51st cross the 50 limit
    }

    private sealed record CreateBody(Guid Id, string HandoffToken, string StatusToken, DateTimeOffset ExpiresAt, string ProfessionalName, string Profession);
}

/// <summary>
/// Shared seeding for the anonymous booking-handoff tests (Tasks 10-14). Insert a real active
/// <see cref="Professional"/> through a service scope; the endpoints are exercised over HTTP.
/// </summary>
internal static class HandoffTestSupport
{
    public static async Task<Guid> SeedActiveProfessionalAsync(
        this ModulesApiFactory factory, string? name = null, string? profession = null)
    {
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create(
            name ?? $"Profissional Handoff {Guid.NewGuid():N}",
            profession ?? "Fisioterapia",
            $"659{Random.Shared.Next(10000000, 99999999)}",
            now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        await db.SaveChangesAsync();
        return professional.Id;
    }
}
