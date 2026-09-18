using GestaoPredio.Application.Whatsapp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Whatsapp;

public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cloud API options (section "Whatsapp") and the single typed client. Nothing here
    /// sends a message on its own: the only caller is the explicit admin test endpoint. The existing
    /// Notifications provider (MetaWhatsAppNotificationService) is intentionally not wired to this client.
    /// </summary>
    public static IServiceCollection AddLumisWhatsApp(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<WhatsAppCloudOptions>, WhatsAppCloudOptionsValidator>();
        var options = services.AddOptions<WhatsAppCloudOptions>()
            .Bind(configuration.GetSection(WhatsAppCloudOptions.SectionName));
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            options.ValidateOnStart();

        // Durable source of truth for outbound messages and their delivery status.
        services.AddScoped<IWhatsAppMessageStore, PostgreSqlWhatsAppMessageStore>();
        // Bounded in-process log of the same updates, for telemetry and tests only.
        services.AddSingleton<WhatsAppStatusRecorder>();

        services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>(client =>
        {
            // The per-call timeout comes from Whatsapp:TimeoutSeconds inside the service, so it can tell a
            // provider timeout apart from the caller cancelling the request.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }
}
