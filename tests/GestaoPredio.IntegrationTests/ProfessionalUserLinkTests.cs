using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalUserLinkTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Get_link_distinguishes_missing_professional_and_absent_or_present_link()
    {
        await PrepareAdminAsync("link-get-admin@lumis.test");
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/admin/professionals/{Guid.NewGuid()}/user-link")).StatusCode);
        var professional = await CreateProfessionalAsync("Sem vínculo");
        var absent = (await (await factory.Client.GetAsync($"/api/admin/professionals/{professional.Id}/user-link"))
            .Content.ReadFromJsonAsync<LinkPayload>())!;
        Assert.False(absent.Linked);
        Assert.Null(absent.UserId);

        var user = await factory.CreateUserAsync("link-get-user@lumis.test", Password, [SystemRoles.Profissional], displayName: "Conta Profissional");
        var linked = await LinkAsync(professional, user.Id);
        Assert.NotEqual(professional.ConcurrencyToken, linked.ConcurrencyToken);
        var presentResponse = await factory.Client.GetAsync($"/api/admin/professionals/{professional.Id}/user-link");
        var present = (await presentResponse.Content.ReadFromJsonAsync<LinkPayload>())!;
        Assert.True(present.Linked);
        Assert.Equal(user.Id, present.UserId);
        Assert.Equal("Conta Profissional", present.DisplayName);
        Assert.Equal("link-get-user@lumis.test", present.Email);
        var json = await presentResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ADMINISTRADOR")]
    [InlineData("GERENTE")]
    [InlineData("PROFISSIONAL,GERENTE")]
    [InlineData("PROFISSIONAL,ADMINISTRADOR")]
    public async Task Put_revalidates_the_exact_ineligible_role_matrix(string rolesText)
    {
        await PrepareAdminAsync($"link-role-admin-{Guid.NewGuid():N}@lumis.test");
        var professional = await CreateProfessionalAsync("Role Matrix");
        var roles = rolesText.Split(',');
        var user = await factory.CreateUserAsync($"link-role-{Guid.NewGuid():N}@lumis.test", Password, roles);

        var response = await factory.PutWithCsrfAsync($"/api/admin/professionals/{professional.Id}/user-link", new
            { applicationUserId = user.Id, concurrencyToken = professional.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PROFESSIONAL_USER", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Link_same_link_replace_and_unlink_follow_noop_and_audit_rules()
    {
        await PrepareAdminAsync("link-lifecycle-admin@lumis.test");
        var professional = await CreateProfessionalAsync("Lifecycle");
        var firstUser = await factory.CreateUserAsync("link-first@lumis.test", Password, [SystemRoles.Profissional]);
        var secondUser = await factory.CreateUserAsync("link-second@lumis.test", Password, [SystemRoles.Profissional]);

        var linked = await LinkAsync(professional, firstUser.Id);
        var same = await LinkAsync(linked, firstUser.Id);
        Assert.Equal(linked.ConcurrencyToken, same.ConcurrencyToken);
        var replaced = await LinkAsync(same, secondUser.Id);
        Assert.NotEqual(same.ConcurrencyToken, replaced.ConcurrencyToken);

        var unlinkedResponse = await factory.DeleteWithCsrfAsync($"/api/admin/professionals/{professional.Id}/user-link",
            new { concurrencyToken = replaced.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, unlinkedResponse.StatusCode);
        var unlinked = (await unlinkedResponse.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.False(unlinked.HasLinkedUser);
        var emptyNoop = await factory.DeleteWithCsrfAsync($"/api/admin/professionals/{professional.Id}/user-link",
            new { concurrencyToken = unlinked.ConcurrencyToken });
        Assert.Equal(unlinked.ConcurrencyToken,
            (await emptyNoop.Content.ReadFromJsonAsync<ProfessionalPayload>())!.ConcurrencyToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditEntries.Where(x => x.Action.StartsWith("PROFESSIONAL_USER_"))
            .OrderBy(x => x.OccurredAt).ToListAsync();
        Assert.Equal(["PROFESSIONAL_USER_LINKED", "PROFESSIONAL_USER_REPLACED", "PROFESSIONAL_USER_UNLINKED"],
            audits.Select(x => x.Action));
        Assert.All(audits, audit =>
        {
            Assert.Equal("PROFESSIONAL", audit.TargetEntityType);
            Assert.Equal(professional.Id, audit.TargetEntityId);
            Assert.NotNull(audit.TargetUserId);
            Assert.Null(audit.ChangedFields);
        });
        var serialized = System.Text.Json.JsonSerializer.Serialize(audits);
        Assert.DoesNotContain("link-first@", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("link-second@", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_inactive_or_already_used_user_is_rejected_with_specific_code()
    {
        await PrepareAdminAsync("link-invalid-admin@lumis.test");
        var firstProfessional = await CreateProfessionalAsync("First");
        var secondProfessional = await CreateProfessionalAsync("Second");
        var inactive = await factory.CreateUserAsync("link-inactive@lumis.test", Password,
            [SystemRoles.Profissional], isActive: false);

        var missing = await factory.PutWithCsrfAsync($"/api/admin/professionals/{firstProfessional.Id}/user-link", new
            { applicationUserId = "missing", concurrencyToken = firstProfessional.ConcurrencyToken });
        Assert.Equal("INVALID_PROFESSIONAL_USER", (await missing.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        var inactiveResult = await factory.PutWithCsrfAsync($"/api/admin/professionals/{firstProfessional.Id}/user-link", new
            { applicationUserId = inactive.Id, concurrencyToken = firstProfessional.ConcurrencyToken });
        Assert.Equal("INVALID_PROFESSIONAL_USER", (await inactiveResult.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var user = await factory.CreateUserAsync("link-used@lumis.test", Password, [SystemRoles.Profissional]);
        await LinkAsync(firstProfessional, user.Id);
        var conflict = await factory.PutWithCsrfAsync($"/api/admin/professionals/{secondProfessional.Id}/user-link", new
            { applicationUserId = user.Id, concurrencyToken = secondProfessional.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("PROFESSIONAL_USER_ALREADY_LINKED", (await conflict.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Stale_link_token_conflicts_before_noop_and_creates_no_false_audit()
    {
        await PrepareAdminAsync("link-stale-admin@lumis.test");
        var professional = await CreateProfessionalAsync("Stale");
        var user = await factory.CreateUserAsync("link-stale-user@lumis.test", Password, [SystemRoles.Profissional]);
        var linked = await LinkAsync(professional, user.Id);
        Assert.NotEqual(professional.ConcurrencyToken, linked.ConcurrencyToken);

        var stale = await factory.PutWithCsrfAsync($"/api/admin/professionals/{professional.Id}/user-link", new
            { applicationUserId = user.Id, concurrencyToken = professional.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_USER_LINKED"));
    }

    [Fact]
    public async Task Two_professionals_cannot_link_the_same_account()
    {
        await PrepareAdminAsync("link-race-admin@lumis.test");
        var first = await CreateProfessionalAsync("Race A");
        var second = await CreateProfessionalAsync("Race B");
        var user = await factory.CreateUserAsync("link-race-user@lumis.test", Password, [SystemRoles.Profissional]);
        var responses = await Task.WhenAll(
            PutLinkAsync(first, user.Id), PutLinkAsync(second, user.Id));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Own_profile_requires_link_and_returns_only_linked_professional()
    {
        await PrepareAdminAsync("own-profile-admin@lumis.test");
        var professional = await CreateProfessionalAsync("Perfil próprio");
        var user = await factory.CreateUserAsync("own-profile@lumis.test", Password, [SystemRoles.Profissional]);
        await LinkAsync(professional, user.Id);
        await factory.LoginAsync("own-profile@lumis.test", Password);
        var response = await factory.Client.GetAsync("/api/professional/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Perfil próprio", json.GetProperty("name").GetString());
        Assert.Equal("Fisio", json.GetProperty("profession").GetString());
        Assert.True(json.TryGetProperty("description", out _));
        Assert.False(json.TryGetProperty("applicationUserId", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client.GetAsync("/api/professional/me/photo")).StatusCode);
        await factory.CreateUserAsync("unlinked-profile@lumis.test", Password, [SystemRoles.Profissional]);
        await factory.LoginAsync("unlinked-profile@lumis.test", Password);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client.GetAsync("/api/professional/me")).StatusCode);
        await factory.LoginAsync("own-profile-admin@lumis.test", Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/professional/me")).StatusCode);
    }
    private async Task PrepareAdminAsync(string email)
    {
        await factory.ResetAsync();
        await factory.CreateUserAsync(email, Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private async Task<ProfessionalPayload> CreateProfessionalAsync(string name)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/professionals", new
            { name, profession = "Fisio", whatsApp = "65999999999" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
    }

    private async Task<ProfessionalPayload> LinkAsync(ProfessionalPayload professional, string userId)
    {
        var response = await PutLinkAsync(professional, userId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
    }

    private Task<HttpResponseMessage> PutLinkAsync(ProfessionalPayload professional, string userId) =>
        factory.PutWithCsrfAsync($"/api/admin/professionals/{professional.Id}/user-link",
            new { applicationUserId = userId, concurrencyToken = professional.ConcurrencyToken });

    private sealed record ProfessionalPayload(Guid Id, bool HasLinkedUser, string ConcurrencyToken);
    private sealed record LinkPayload(bool Linked, string? UserId, string? DisplayName, string? Email);
    private sealed record ErrorPayload(string Code, string Message);
}
