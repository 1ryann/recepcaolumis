using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Rooms;

namespace GestaoPredio.IntegrationTests;

public sealed class WhatsappConfigurationTests
{
    [Fact]
    public void Staging_with_empty_phone_fails_during_startup()
    {
        using var api = CreateApi("Staging", "");

        var exception = Assert.Throws<OptionsValidationException>(() => api.CreateClient());

        Assert.Contains("WhatsApp financeiro", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("+", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Testing_factory_starts_with_a_fictitious_phone()
    {
        using var api = CreateApi("Testing", "+5569999999999");
        using var client = api.CreateClient();

        var options = api.Services.GetRequiredService<IOptions<WhatsappOptions>>().Value;
        Assert.Equal("+5569999999999", options.FinanceiroPhoneNumber);
    }

    private static WebApplicationFactory<recepcaototem.Pages.IndexModel> CreateApi(string environmentName, string phone) =>
        new WebApplicationFactory<recepcaototem.Pages.IndexModel>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "");
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Security:DataProtectionPath", Path.Combine(Path.GetTempPath(), "Lumis-Whatsapp-Test-Keys"));
            builder.UseSetting("Storage:PrivateFilesPath", Path.GetTempPath());
            builder.UseSetting("Whatsapp:FinanceiroPhoneNumber", phone);
        });
}
