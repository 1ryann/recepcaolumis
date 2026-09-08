using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalPresenceApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Professional_issues_a_short_lived_single_row_qr_token()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, professional) = await SeedLinkedProfessionalAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);

        var before = DateTimeOffset.UtcNow;
        var first = await factory.PostWithCsrfAsync("/api/professional/presence/qr", new { });
        first.EnsureSuccessStatusCode();
        var firstPayload = await first.Content.ReadFromJsonAsync<QrPayload>();
        Assert.NotNull(firstPayload);
        Assert.InRange((firstPayload!.ExpiresAt - before).TotalSeconds, 90, 150);
        Assert.Equal(32, WebEncoders.Base64UrlDecode(firstPayload.Token).Length);

        var second = await factory.PostWithCsrfAsync("/api/professional/presence/qr", new { });
        second.EnsureSuccessStatusCode();
        var secondPayload = (await second.Content.ReadFromJsonAsync<QrPayload>())!;
        Assert.NotEqual(firstPayload.Token, secondPayload.Token);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ProfessionalPresenceTokens.CountAsync(x => x.ProfessionalId == professional.Id));
        Assert.True(await db.AuditEntries.AnyAsync(x =>
            x.Action == "PROFESSIONAL_PRESENCE_QR_ISSUED" && x.TargetEntityId == professional.Id));
    }

    [Fact]
    public async Task Own_status_is_absent_before_any_scan()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, _) = await SeedLinkedProfessionalAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);

        var status = await factory.Client.GetFromJsonAsync<StatusPayload>("/api/professional/presence");
        Assert.Equal("ABSENT", status!.Status);
        Assert.Null(status.Since);
        Assert.Null(status.AbsentUntil);
    }

    [Fact]
    public async Task Own_status_reflects_an_open_effective_presence()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, professional) = await SeedLinkedProfessionalAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(professional.Id, DateTimeOffset.UtcNow.AddMinutes(-5)));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);

        var status = await factory.Client.GetFromJsonAsync<StatusPayload>("/api/professional/presence");
        Assert.Equal("PRESENT", status!.Status);
        Assert.NotNull(status.Since);
    }

    [Fact]
    public async Task Qr_issuance_requires_the_professional_policy()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.PostAsJsonAsync("/api/professional/presence/qr", new { })).StatusCode);

        var customerEmail = $"customer-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(customerEmail, Password, [SystemRoles.Customer]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(customerEmail, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/professional/presence/qr", new { })).StatusCode);
    }

    [Fact]
    public async Task Qr_issuance_for_a_role_without_a_professional_link_is_not_found()
    {
        await factory.ResetAsync();
        var email = $"unlinked-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);

        var response = await factory.PostWithCsrfAsync("/api/professional/presence/qr", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(string Email, Professional Professional)> SeedLinkedProfessionalAsync()
    {
        var email = $"presence-{Guid.NewGuid():N}@lumis.test";
        var user = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        var professional = Professional.Create("Presença API", "Fisioterapia",
            $"699{Random.Shared.Next(10000000, 99999999)}", DateTimeOffset.UtcNow);
        professional.LinkUser(user.Id, DateTimeOffset.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        await db.SaveChangesAsync();
        return (email, professional);
    }

    private sealed record QrPayload(string Token, DateTimeOffset ExpiresAt);
    private sealed record StatusPayload(string Status, DateTimeOffset? Since, DateTimeOffset? AbsentUntil);
}
