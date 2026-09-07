using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalMutationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Create_requires_operations_and_antiforgery()
    {
        await factory.ResetAsync();
        var body = new { name = "Ana", profession = "Psicóloga", whatsApp = "65999999999" };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.PostAsJsonAsync("/api/admin/professionals", body)).StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "professional-create@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/professionals", body)).StatusCode);

        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "manager-create@lumis.test");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/admin/professionals", body)).StatusCode);
    }

    [Fact]
    public async Task Create_normalizes_persists_active_and_audits_without_personal_values()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-create@lumis.test");

        var response = await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = "  Ána   Silva ", profession = " Fisióterapia ", whatsApp = "(65) 99999-9999",
            description = "  Atendimento clínico  "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.True(created.IsActive);
        Assert.Equal("+5565999999999", created.WhatsApp);
        Assert.Equal("Atendimento clínico", created.Description);
        Assert.NotEmpty(created.ConcurrencyToken);
        Assert.Equal($"/api/admin/professionals/{created.Id}", response.Headers.Location?.ToString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Professionals.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("ANA SILVA", stored.NormalizedName);
        Assert.Equal("FISIOTERAPIA", stored.NormalizedProfession);
        var audit = await db.AuditEntries.SingleAsync(x => x.Action == "PROFESSIONAL_CREATED");
        Assert.Equal("PROFESSIONAL", audit.TargetEntityType);
        Assert.Equal(created.Id, audit.TargetEntityId);
        Assert.Null(audit.ChangedFields);
        Assert.DoesNotContain("+5565", System.Text.Json.JsonSerializer.Serialize(audit));
    }

    [Fact]
    public async Task Description_is_exposed_consistently_by_reception_totem_and_customer_projections()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"admin-description-{Guid.NewGuid():N}@lumis.test");

        var create = await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = "Profissional Descrição", profession = "Fisioterapia", whatsApp = "65999999999",
            description = "  Atendimento especializado  "
        });
        var created = (await create.Content.ReadFromJsonAsync<ProfessionalPayload>())!;

        var reception = await factory.Client.GetAsync("/api/reception/professionals");
        Assert.Contains("Atendimento especializado", await reception.Content.ReadAsStringAsync());
        var totem = await factory.Client.GetAsync("/api/totem/professionals");
        Assert.Contains("Atendimento especializado", await totem.Content.ReadAsStringAsync());

        var email = $"customer-description-{Guid.NewGuid():N}@lumis.test";
        var register = await factory.PostWithCsrfAsync("/api/customer/register", new
        {
            name = "Cliente Descrição", phone = "69988887777", email,
            password = Password, confirmation = Password
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
        var customer = await factory.Client.GetAsync("/api/customer/professionals");
        Assert.Contains("Atendimento especializado", await customer.Content.ReadAsStringAsync());
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Theory]
    [InlineData("", "Psicóloga", "65999999999")]
    [InlineData("Ana", "", "65999999999")]
    [InlineData("Ana", "Psicóloga", "invalid")]
    public async Task Invalid_create_is_rejected(string name, string profession, string whatsApp)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, $"manager-invalid-{Guid.NewGuid():N}@lumis.test");
        var response = await factory.PostWithCsrfAsync("/api/admin/professionals", new { name, profession, whatsApp });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PROFESSIONAL", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Create_rejects_status_overposting_and_allows_identical_duplicates()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-duplicates@lumis.test");
        var overposted = await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = "Ana", profession = "Fisio", whatsApp = "65999999999", isActive = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, overposted.StatusCode);

        var body = new { name = "Ana", profession = "Fisio", whatsApp = "65999999999" };
        var first = await factory.PostWithCsrfAsync("/api/admin/professionals", body);
        var second = await factory.PostWithCsrfAsync("/api/admin/professionals", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual((await first.Content.ReadFromJsonAsync<ProfessionalPayload>())!.Id,
            (await second.Content.ReadFromJsonAsync<ProfessionalPayload>())!.Id);
    }

    [Fact]
    public async Task Create_enforces_input_lengths_and_accepts_the_exact_limits()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-lengths@lumis.test");

        Assert.Equal(HttpStatusCode.Created, (await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = new string('N', 200), profession = new string('P', 150), whatsApp = "65999999999"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = new string('N', 201), profession = "Fisio", whatsApp = "65999999999"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/professionals", new
        {
            name = "Ana", profession = new string('P', 151), whatsApp = "65999999999"
        })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public async Task Edit_rejects_missing_or_structurally_invalid_token(string? token)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, $"manager-token-{Guid.NewGuid():N}@lumis.test");
        var created = await CreateAsync("Ana", "Fisio", "65999999999");

        var response = await factory.PutWithCsrfAsync($"/api/admin/professionals/{created.Id}", new
        {
            name = "Ana", profession = "Fisio", whatsApp = "65999999999", concurrencyToken = token
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CONCURRENCY_TOKEN", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Edit_updates_derived_values_returns_new_token_and_audits_field_names_only()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "manager-edit@lumis.test");
        var created = await CreateAsync("Ana", "Fisio", "65999999999");

        var response = await factory.PutWithCsrfAsync($"/api/admin/professionals/{created.Id}", new
        {
            name = " Bêatriz ", profession = " Terapía ", whatsApp = "(65) 98888-7777",
            description = "  Nova descrição  ", concurrencyToken = created.ConcurrencyToken
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Professionals.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("BEATRIZ", stored.NormalizedName);
        Assert.Equal("TERAPIA", stored.NormalizedProfession);
        var audit = await db.AuditEntries.SingleAsync(x => x.Action == "PROFESSIONAL_UPDATED");
        Assert.Equal("Description,Name,Profession,WhatsApp", audit.ChangedFields);
        Assert.Equal("Nova descrição", updated.Description);
        Assert.DoesNotContain("+5565", audit.ChangedFields!);
    }

    [Fact]
    public async Task Edit_with_current_values_is_noop_but_stale_token_conflicts_first()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "admin-noop@lumis.test");
        var created = await CreateAsync("Ana", "Fisio", "65999999999");
        var body = new { name = "Ana", profession = "Fisio", whatsApp = "+5565999999999", concurrencyToken = created.ConcurrencyToken };

        var noop = await factory.PutWithCsrfAsync($"/api/admin/professionals/{created.Id}", body);
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        var unchanged = (await noop.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.Equal(created.ConcurrencyToken, unchanged.ConcurrencyToken);
        Assert.Equal(created.UpdatedAt, unchanged.UpdatedAt);

        var changed = await factory.PutWithCsrfAsync($"/api/admin/professionals/{created.Id}", new
        {
            name = "Ana Nova", profession = "Fisio", whatsApp = "+5565999999999",
            concurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var stale = await factory.PutWithCsrfAsync($"/api/admin/professionals/{created.Id}", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_UPDATED"));
    }

    private async Task<ProfessionalPayload> CreateAsync(string name, string profession, string whatsApp)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/professionals", new { name, profession, whatsApp });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record ProfessionalPayload(Guid Id, string Name, string Profession, string WhatsApp,
        bool IsActive, DateTimeOffset UpdatedAt, string ConcurrencyToken, string? Description);
    private sealed record ErrorPayload(string Code, string Message);
}
