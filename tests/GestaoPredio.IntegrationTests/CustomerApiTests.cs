using System.Net;
using System.Net.Http.Json;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class CustomerApiTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Remote_registration_creates_customer_account_and_me_is_scoped_to_principal()
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/register")
        {
            Content = JsonContent.Create(new { name = "  Cliente   Teste ", phone = "(69) 99999-9999", email = $"customer-{Guid.NewGuid():N}@lumis.test", password = "Valid-Password-123!", confirmation = "Valid-Password-123!" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await factory.Client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, responseBody);
        var me = await factory.Client.GetAsync("/api/customer/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Totem_lookup_masks_existing_customer_without_exposing_identity()
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/customer/register")
        {
            Content = JsonContent.Create(new { name = "Carlos Oliveira", phone = "(69) 99999-9999", email = $"lookup-{Guid.NewGuid():N}@lumis.test", password = "Valid-Password-123!", confirmation = "Valid-Password-123!" })
        };
        register.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.Created, (await factory.Client.SendAsync(register)).StatusCode);
        var lookup = await factory.Client.PostAsJsonAsync("/api/totem/customers/resolve", new { name = "Carlos", phone = "69999999999" });
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var body = await lookup.Content.ReadAsStringAsync();
        Assert.Contains("Carlos O.", body);
        Assert.DoesNotContain("+5569999999999", body);
    }
}
