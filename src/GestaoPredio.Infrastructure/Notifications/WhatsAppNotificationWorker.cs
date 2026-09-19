using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Notifications;

/// <summary>
/// Runs <see cref="WhatsAppNotificationDispatcher.RunOnceAsync"/> every <c>PollIntervalSeconds</c> while
/// Whatsapp:Notifications:Enabled is true. It is re-read each cycle, so dispatch can be switched on or off without a
/// restart. A failing cycle is logged and the next one runs normally; business operations never wait on it.
/// </summary>
public sealed class WhatsAppNotificationWorker(
    IServiceScopeFactory scopes,
    IOptionsMonitor<WhatsAppNotificationOptions> options,
    ILogger<WhatsAppNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasEnabled = (bool?)null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;
            if (wasEnabled != settings.Enabled)
            {
                logger.LogInformation("WhatsApp notification dispatch {State}.", settings.Enabled ? "enabled" : "disabled");
                wasEnabled = settings.Enabled;
            }

            if (settings.Enabled)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var summary = await scope.ServiceProvider.GetRequiredService<WhatsAppNotificationDispatcher>()
                        .RunOnceAsync(stoppingToken);
                    if (summary.Claimed > 0 || summary.DelayNoticesQueued > 0 || summary.Unconfirmed > 0 || summary.Failed > 0)
                        logger.LogInformation(
                            "WhatsApp notification cycle. Queued: {Queued}; Claimed: {Claimed}; Accepted: {Accepted}; Retried: {Retried}; Failed: {Failed}; Skipped: {Skipped}; Unconfirmed: {Unconfirmed}",
                            summary.DelayNoticesQueued, summary.Claimed, summary.Accepted, summary.Retried, summary.Failed, summary.Skipped, summary.Unconfirmed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "WhatsApp notification cycle failed; retrying next interval.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
