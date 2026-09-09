using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemPresenceApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Confirm_starts_presence_and_is_reflected_in_own_status()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, professional) = await SeedLinkedProfessionalAsync();
        var token = await IssueQrAsync(email);

        var confirm = await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token });
        confirm.EnsureSuccessStatusCode();
        Assert.Equal("PRESENT", (await confirm.Content.ReadFromJsonAsync<PresencePayload>())!.Status);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.ProfessionalPresences
                .CountAsync(x => x.ProfessionalId == professional.Id && x.EndedAt == null));
            Assert.True(await db.AuditEntries.AnyAsync(x =>
                x.Action == "PROFESSIONAL_PRESENCE_STARTED" && x.TargetEntityType == "PROFESSIONAL_PRESENCE"));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
        var status = await factory.Client.GetFromJsonAsync<StatusPayload>("/api/professional/presence");
        Assert.Equal("PRESENT", status!.Status);
    }

    [Fact]
    public async Task Second_confirm_with_the_same_token_is_rejected_generically()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, _) = await SeedLinkedProfessionalAsync();
        var token = await IssueQrAsync(email);

        (await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token })).EnsureSuccessStatusCode();
        var second = await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("INVALID_PRESENCE", (await second.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Confirm_while_already_present_is_idempotent()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, professional) = await SeedLinkedProfessionalAsync();

        (await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token = await IssueQrAsync(email) }))
            .EnsureSuccessStatusCode();
        var again = await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token = await IssueQrAsync(email) });
        again.EnsureSuccessStatusCode();
        Assert.Equal("PRESENT", (await again.Content.ReadFromJsonAsync<PresencePayload>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ProfessionalPresences
            .CountAsync(x => x.ProfessionalId == professional.Id && x.EndedAt == null));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x =>
            x.Action == "PROFESSIONAL_PRESENCE_STARTED" && x.TargetEntityType == "PROFESSIONAL_PRESENCE"));
    }

    [Fact]
    public async Task Confirm_with_an_expired_token_is_rejected()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (_, professional) = await SeedLinkedProfessionalAsync();
        var raw = RandomNumberGenerator.GetBytes(32);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.ProfessionalPresenceTokens.Add(ProfessionalPresenceToken.Create(
                professional.Id, SHA256.HashData(raw), now.AddMinutes(-10), now.AddMinutes(-5)));
            await db.SaveChangesAsync();
        }

        var response = await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm",
            new { token = WebEncoders.Base64UrlEncode(raw) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PRESENCE", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Confirm_rejects_a_malformed_token()
    {
        await factory.ResetAsync();
        var response = await factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token = "not a token!!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PRESENCE", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Immediate_lists_only_present_and_centrally_available_professionals()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var now = DateTimeOffset.UtcNow;
        var present = Professional.Create("Presente Livre", "Fisioterapia", "69990000001", now);
        var absent = Professional.Create("Ausente", "Psicologia", "69990000002", now);
        var blocked = Professional.Create("Presente Bloqueado", "Nutrição", "69990000003", now);
        var room = Room.Create("Sala Totem", null, 4, 90m, now);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")).DateTime);
        var blockedException = ProfessionalAvailabilityException.Create(blocked.Id, localToday, true, null, null, null, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(present, absent, blocked, room, blockedException);
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(present.Id, now.AddMinutes(-20)));
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(blocked.Id, now.AddMinutes(-20)));
            await db.SaveChangesAsync();
        }

        var listed = await factory.Client.GetFromJsonAsync<ProfessionalPayload[]>(
            "/api/totem/immediate?durationMinutes=15");

        Assert.Contains(listed!, x => x.Id == present.Id);
        Assert.DoesNotContain(listed!, x => x.Id == absent.Id);
        Assert.DoesNotContain(listed!, x => x.Id == blocked.Id);
    }

    [Fact]
    public async Task Parallel_confirms_of_the_same_token_yield_one_presence_and_one_audit_row()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, professional) = await SeedLinkedProfessionalAsync();
        var token = await IssueQrAsync(email);

        var first = factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token });
        var second = factory.Client.PostAsJsonAsync("/api/totem/presence/confirm", new { token });
        var responses = await Task.WhenAll(first, second);

        Assert.Contains(responses, r => r.IsSuccessStatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ProfessionalPresences
            .CountAsync(x => x.ProfessionalId == professional.Id && x.EndedAt == null));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x =>
            x.Action == "PROFESSIONAL_PRESENCE_STARTED" && x.TargetEntityType == "PROFESSIONAL_PRESENCE"));
    }

    private async Task<string> IssueQrAsync(string email)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
        var response = await factory.PostWithCsrfAsync("/api/professional/presence/qr", new { });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QrPayload>())!.Token;
    }

    private async Task<(string Email, Professional Professional)> SeedLinkedProfessionalAsync()
    {
        var email = $"totem-presence-{Guid.NewGuid():N}@lumis.test";
        var user = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        var professional = Professional.Create("Totem Presença", "Fisioterapia",
            $"699{Random.Shared.Next(10000000, 99999999)}", DateTimeOffset.UtcNow);
        professional.LinkUser(user.Id, DateTimeOffset.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        await db.SaveChangesAsync();
        return (email, professional);
    }

    private sealed record QrPayload(string Token, DateTimeOffset ExpiresAt);
    private sealed record PresencePayload(string Status);
    private sealed record StatusPayload(string Status, DateTimeOffset? Since, DateTimeOffset? AbsentUntil);
    private sealed record ProfessionalPayload(Guid Id, string Name, string Profession, string? Description);
    private sealed record ErrorPayload(string Code, string Message);
}
