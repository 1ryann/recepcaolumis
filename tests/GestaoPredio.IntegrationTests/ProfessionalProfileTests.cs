using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalProfileTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Get_me_returns_whats_app_and_concurrency_token()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync();

        var response = await factory.Client.GetAsync("/api/professional/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("whatsApp", out var whatsApp));
        Assert.False(string.IsNullOrEmpty(whatsApp.GetString()));
        Assert.True(body.TryGetProperty("concurrencyToken", out _));
    }

    [Fact]
    public async Task Put_me_updates_whats_app_and_description_and_keeps_name_profession()
    {
        await factory.ResetAsync();
        var professional = await CreateLinkedProfessionalAsync();
        var getResponse = await factory.Client.GetAsync("/api/professional/me");
        var current = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = current.GetProperty("concurrencyToken").GetString();

        var putResponse = await factory.PutWithCsrfAsync("/api/professional/me", new
        {
            whatsApp = "11988887777",
            description = "Atendimento humanizado.",
            concurrencyToken = token,
        });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("+5511988887777", updated.GetProperty("whatsApp").GetString());
        Assert.Equal("Atendimento humanizado.", updated.GetProperty("description").GetString());
        Assert.Equal(professional.Name, updated.GetProperty("name").GetString());
        Assert.Equal(professional.Profession, updated.GetProperty("profession").GetString());
        Assert.NotEqual(token, updated.GetProperty("concurrencyToken").GetString());
    }

    [Fact]
    public async Task Put_me_rejects_unmapped_fields_like_professional_id()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync();

        var response = await factory.PutWithCsrfAsync("/api/professional/me", new
        {
            whatsApp = "11988887777",
            concurrencyToken = "x",
            professionalId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_me_with_invalid_whats_app_returns_invalid_professional()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync();
        var getResponse = await factory.Client.GetAsync("/api/professional/me");
        var token = (await getResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyToken").GetString();

        var response = await factory.PutWithCsrfAsync("/api/professional/me",
            new { whatsApp = "123", concurrencyToken = token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_PROFESSIONAL", body.GetProperty("code").GetString());
    }

    private async Task<Professional> CreateLinkedProfessionalAsync()
    {
        var user = await factory.CreateUserAsync($"prof-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create("Ana Souza", "Fisioterapia", "+5511999999999", now);
        professional.LinkUser(user.Id, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);
        return professional;
    }
}
