using System.Collections.Concurrent;
using GestaoPredio.Application.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GestaoPredio.Infrastructure.Notifications;

public sealed record DemoNotificationAttempt(
    Guid ProfessionalId,
    string EventType,
    string Provider,
    bool Success,
    string? FailureCode);

public sealed class DemoNotificationRecorder
{
    private readonly ConcurrentQueue<DemoNotificationAttempt> attempts = new();

    public bool ForceFailure { get; set; }

    public IReadOnlyList<DemoNotificationAttempt> Attempts => attempts.ToArray();

    internal void Record(DemoNotificationAttempt attempt) => attempts.Enqueue(attempt);

    public void Clear()
    {
        ForceFailure = false;
        while (attempts.TryDequeue(out _)) { }
    }
}

public sealed class DemoNotificationService(
    DemoNotificationRecorder recorder,
    IConfiguration configuration) : INotificationProvider
{
    public const string ProviderName = "Demo";
    public string Name => ProviderName;

    public Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var forcedFailure = recorder.ForceFailure || configuration.GetValue("Notifications:Demo:ForceFailure", false);
        var result = forcedFailure
            ? NotificationResult.Failed(Name, "DEMO_PROVIDER_FAILURE")
            : NotificationResult.Succeeded(Name);
        recorder.Record(new DemoNotificationAttempt(message.ProfessionalId, message.EventType,
            Name, result.Success, result.FailureCode));
        return Task.FromResult(result);
    }
}

public sealed class MetaWhatsAppNotificationService(
    IConfiguration configuration) : INotificationProvider
{
    public const string ProviderName = "Meta";
    public string Name => ProviderName;

    public Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The adapter is intentionally fail-closed until a future implementation
        // adds the external HTTP call behind explicit production configuration.
        var configured = !string.IsNullOrWhiteSpace(configuration["Notifications:Meta:BaseUrl"])
            && !string.IsNullOrWhiteSpace(configuration["Notifications:Meta:PhoneNumberId"])
            && !string.IsNullOrWhiteSpace(configuration["Notifications:Meta:AccessToken"]);
        return Task.FromResult(NotificationResult.Failed(Name,
            configured ? "META_PROVIDER_DISABLED" : "META_NOT_CONFIGURED"));
    }
}

public sealed class NotificationService(
    ApplicationDbContext db,
    INotificationProvider provider,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<NotificationResult> NotifyProfessionalAsync(
        ProfessionalNotificationEvent notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var professional = await db.Professionals.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == notification.ProfessionalId, cancellationToken);
            if (professional is null || !professional.IsActive)
            {
                var failure = NotificationResult.Failed(provider.Name, "RECIPIENT_UNAVAILABLE");
                LogFailure(notification, failure);
                return failure;
            }

            if (!WhatsAppNormalizer.TryNormalize(professional.WhatsApp, out var phone))
            {
                var failure = NotificationResult.Failed(provider.Name, "RECIPIENT_PHONE_INVALID");
                LogFailure(notification, failure);
                return failure;
            }

            var firstName = FirstName(notification.VisitorName);
            var body = $"Novo cliente aguardando atendimento: {firstName}. Chegada: {notification.ArrivedAt:yyyy-MM-dd HH:mm zzz}.";
            var result = await provider.SendAsync(new NotificationMessage(
                notification.ProfessionalId, phone, notification.EventType, body), cancellationToken);
            LogFailure(notification, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var result = NotificationResult.Failed(provider.Name, "NOTIFICATION_CANCELLED");
            LogFailure(notification, result);
            return result;
        }
        catch
        {
            var result = NotificationResult.Failed(provider.Name, "PROVIDER_FAILURE");
            LogFailure(notification, result);
            return result;
        }
    }

    private void LogFailure(ProfessionalNotificationEvent notification, NotificationResult result)
    {
        if (!result.Success)
            logger.LogWarning("Notification failed. Provider: {Provider}; EventType: {EventType}; ProfessionalId: {ProfessionalId}; FailureCode: {FailureCode}",
                result.Provider, notification.EventType, notification.ProfessionalId, result.FailureCode);
    }

    private static string FirstName(string value)
    {
        var first = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "cliente" : first.Length > 80 ? first[..80] : first;
    }
}

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddLumisNotifications(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var provider = configuration["Notifications:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
            provider = environment.IsDevelopment() || environment.EnvironmentName == "Testing" ? "Demo" : "Meta";

        if (provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<DemoNotificationRecorder>();
            services.AddSingleton<DemoNotificationService>();
            services.AddSingleton<INotificationProvider>(sp => sp.GetRequiredService<DemoNotificationService>());
        }
        else if (provider.Equals("Meta", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<MetaWhatsAppNotificationService>();
            services.AddSingleton<INotificationProvider>(sp => sp.GetRequiredService<MetaWhatsAppNotificationService>());
        }
        else
        {
            throw new InvalidOperationException("Notifications:Provider must be Demo or Meta.");
        }

        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }
}
