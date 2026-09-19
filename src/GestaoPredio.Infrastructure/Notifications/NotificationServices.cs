using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Operational WhatsApp notifications. Business endpoints only add a <c>WhatsAppNotification</c> row inside their
    /// own transaction; the dispatcher sends it later through the single <c>IWhatsAppService</c>
    /// (<c>WhatsAppCloudApiService</c>) and the existing webhook tracks delivery. This replaces the former synchronous
    /// Demo/Meta providers, whose Meta adapter never called the Cloud API.
    /// </summary>
    public static IServiceCollection AddLumisNotifications(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<WhatsAppNotificationOptions>, WhatsAppNotificationOptionsValidator>();
        var notificationOptions = services.AddOptions<WhatsAppNotificationOptions>()
            .Bind(configuration.GetSection(WhatsAppNotificationOptions.SectionName));
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            notificationOptions.ValidateOnStart();
        services.AddOptions<WhatsAppTemplateOptions>().Bind(configuration.GetSection(WhatsAppTemplateOptions.SectionName));

        services.AddScoped<WhatsAppNotificationComposer>();
        services.AddScoped<WhatsAppNotificationDispatcher>();
        services.AddScoped<WhatsAppOptInService>();
        services.AddHostedService<WhatsAppNotificationWorker>();
        return services;
    }
}
