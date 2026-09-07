using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.ProfessionalRegistrations;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalRegistrationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Applicant_registers_logs_in_and_manager_approves_transactionally()
    {
        await factory.ResetAsync();
        var register = await factory.PostWithCsrfAsync("/api/professional-registration/register", new
        { name = "Ana Lima", profession = "Fisioterapia", whatsApp = "(69) 99999-9999", email = "applicant@lumis.test", password = Password, confirmation = Password, description = "Atendimento clínico." });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("applicant@lumis.test", Password)).StatusCode);
        var session = await factory.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/session");
        Assert.Contains(SystemRoles.ProfessionalApplicant, session.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        var mine = await factory.Client.GetFromJsonAsync<ApplicationPayload>("/api/professional-registration/me");
        Assert.Equal("PENDING", mine!.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/professional/me")).StatusCode);

        var manager = await factory.CreateUserAsync("manager-applications@lumis.test", Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("manager-applications@lumis.test", Password)).StatusCode);
        var page = await factory.Client.GetFromJsonAsync<PagePayload>("/api/reception/professional-applications?status=PENDING");
        Assert.Single(page!.Items);
        var approved = await factory.PostWithCsrfAsync($"/api/reception/professional-applications/{mine.Id}/approve", new { concurrencyToken = mine.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("applicant@lumis.test");
        Assert.NotNull(await db.Professionals.SingleOrDefaultAsync(x => x.ApplicationUserId == user!.Id));
        Assert.Equal([SystemRoles.Profissional], await users.GetRolesAsync(user!));
        Assert.Contains(await db.AuditEntries.ToListAsync(), x => x.Action == "PROFESSIONAL_APPLICATION_APPROVED" && x.ActorUserId == manager.Id);
    }

    [Fact]
    public async Task Rejection_preserves_account_without_professional_and_stale_review_conflicts()
    {
        await factory.ResetAsync();
        await factory.PostWithCsrfAsync("/api/professional-registration/register", new
        { name = "Bia Souza", profession = "Psicologia", whatsApp = "69988887777", email = "rejected@lumis.test", password = Password, confirmation = Password, description = (string?)null });
        await factory.LoginAsync("rejected@lumis.test", Password);
        var mine = await factory.Client.GetFromJsonAsync<ApplicationPayload>("/api/professional-registration/me");
        await factory.CreateUserAsync("manager-reject@lumis.test", Password, [SystemRoles.Gerente]);
        await factory.LoginAsync("manager-reject@lumis.test", Password);
        var rejected = await factory.PostWithCsrfAsync($"/api/reception/professional-applications/{mine!.Id}/reject", new { concurrencyToken = mine.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var stale = await factory.PostWithCsrfAsync($"/api/reception/professional-applications/{mine.Id}/approve", new { concurrencyToken = mine.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Professionals.ToListAsync());
    }

    private sealed record ApplicationPayload(Guid Id, string Status, string ConcurrencyToken);
    private sealed record PagePayload(ApplicationPayload[] Items, int TotalCount);
}
