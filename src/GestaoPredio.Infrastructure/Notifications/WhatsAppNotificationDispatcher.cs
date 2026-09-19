using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Notifications;

public sealed record WhatsAppDispatchSummary(int Claimed, int Accepted, int Retried, int Failed, int Skipped);

/// <summary>
/// One dispatch cycle: claim and send due notifications. Safe to run on several instances at once: a notification
/// is claimed by a single UPDATE … FOR UPDATE SKIP LOCKED, so two dispatchers never hold the same row.
/// Delivery is at-least-once only in one corner: if the process dies after Meta accepted but before the wamid is
/// stored, the lease expires and the message is sent again. Everything else is exactly once.
/// </summary>
public sealed class WhatsAppNotificationDispatcher(
    ApplicationDbContext db,
    WhatsAppNotificationComposer composer,
    IWhatsAppService whatsApp,
    IOptionsMonitor<WhatsAppNotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<WhatsAppNotificationDispatcher> logger)
{
    /// <summary>Provider failures worth another attempt. Anything else is permanent for this message.</summary>
    private static readonly HashSet<string> TransientFailures =
    [
        WhatsAppFailureCodes.ProviderUnavailable,
        WhatsAppFailureCodes.Timeout,
        WhatsAppFailureCodes.NetworkError,
        WhatsAppFailureCodes.RateLimited
    ];

    public async Task<WhatsAppDispatchSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var ids = await ClaimAsync(cancellationToken);
        int accepted = 0, retried = 0, failed = 0, skipped = 0;
        foreach (var id in ids)
        {
            switch (await ProcessAsync(id, cancellationToken))
            {
                case WhatsAppNotificationStatus.Accepted: accepted++; break;
                case WhatsAppNotificationStatus.Pending: retried++; break;
                case WhatsAppNotificationStatus.Failed: failed++; break;
                case WhatsAppNotificationStatus.Skipped: skipped++; break;
            }
        }
        return new WhatsAppDispatchSummary(ids.Count, accepted, retried, failed, skipped);
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        // UTC, truncated to PostgreSQL's microsecond precision like every stored timestamp.
        var utc = timeProvider.GetUtcNow().ToUniversalTime();
        var now = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        var lockedUntil = now.AddSeconds(settings.LeaseSeconds);
        var batch = settings.BatchSize;
        // Mirrors WhatsAppNotification.Claim: PENDING and due, or PROCESSING with an expired lease (crashed worker).
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE "WhatsAppNotifications" AS n
            SET "Status" = 'PROCESSING', "LockedUntil" = {lockedUntil}, "Attempts" = n."Attempts" + 1, "UpdatedAt" = {now}
            WHERE n."Id" IN (
                SELECT c."Id" FROM "WhatsAppNotifications" AS c
                WHERE (c."Status" = 'PENDING' AND c."NextAttemptAt" <= {now})
                   OR (c."Status" = 'PROCESSING' AND c."LockedUntil" < {now})
                ORDER BY c."NextAttemptAt"
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED)
            RETURNING n."Id" AS "Value"
            """).ToListAsync(cancellationToken);
    }

    private async Task<WhatsAppNotificationStatus?> ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var notification = await db.WhatsAppNotifications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (notification is null || notification.Status != WhatsAppNotificationStatus.Processing) return null;
        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        try
        {
            if (notification.Attempts > settings.MaxAttempts)
                return await FinishAsync(notification, WhatsAppNotificationStatus.Failed, WhatsAppNotificationCodes.MaxAttemptsExceeded, cancellationToken);
            if (now - notification.CreatedAt > MaxAge(notification.Type, settings))
                return await FinishAsync(notification, WhatsAppNotificationStatus.Skipped, WhatsAppNotificationCodes.Expired, cancellationToken);

            var composition = await composer.ComposeAsync(notification, now, cancellationToken);
            if (composition.SkipCode is { } skip)
                return await FinishAsync(notification, WhatsAppNotificationStatus.Skipped, skip, cancellationToken);
            if (composition.FailCode is { } fail)
                return await FinishAsync(notification, WhatsAppNotificationStatus.Failed, fail, cancellationToken);

            var result = await whatsApp.SendTemplateAsync(composition.Phone!, composition.Template!, cancellationToken);
            now = timeProvider.GetUtcNow();
            if (result is { Success: true, MessageId: { } messageId })
            {
                notification.MarkAccepted(messageId, now);
                await CatchUpDeliveryStatusAsync(notification, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                Log(notification, "ACCEPTED");
                return notification.Status is WhatsAppNotificationStatus.Failed ? WhatsAppNotificationStatus.Failed : WhatsAppNotificationStatus.Accepted;
            }

            var code = result.FailureCode ?? WhatsAppFailureCodes.RequestRejected;
            if (TransientFailures.Contains(code) && notification.Attempts < settings.MaxAttempts)
            {
                notification.ScheduleRetry(code, now + settings.RetryDelayBefore(notification.Attempts + 1), now);
                await db.SaveChangesAsync(cancellationToken);
                Log(notification, "RETRY_SCHEDULED");
                return WhatsAppNotificationStatus.Pending;
            }
            return await FinishAsync(notification, WhatsAppNotificationStatus.Failed, code, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never lose the notification: put it back with a delay. If even that fails, the lease expires and a
            // later cycle reclaims it.
            logger.LogWarning(exception, "WhatsApp notification dispatch error. NotificationId: {NotificationId}; Type: {Type}; Attempt: {Attempt}",
                notification.Id, notification.Type, notification.Attempts);
            try
            {
                db.ChangeTracker.Clear();
                var fresh = await db.WhatsAppNotifications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (fresh is { Status: WhatsAppNotificationStatus.Processing })
                {
                    if (fresh.Attempts < settings.MaxAttempts)
                        fresh.ScheduleRetry(WhatsAppNotificationCodes.DispatchError, now + settings.RetryDelayBefore(fresh.Attempts + 1), now);
                    else
                        fresh.MarkFailed(WhatsAppNotificationCodes.DispatchError, now);
                    await db.SaveChangesAsync(cancellationToken);
                    return fresh.Status;
                }
            }
            catch (Exception inner) when (inner is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(inner, "WhatsApp notification could not be rescheduled; the lease will expire. NotificationId: {NotificationId}", id);
            }
            return null;
        }
    }

    /// <summary>The webhook may have reported sent/delivered/read/failed before this wamid was stored here.</summary>
    private async Task CatchUpDeliveryStatusAsync(WhatsAppNotification notification, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var message = await db.WhatsAppMessages.AsNoTracking()
            .Where(x => x.MessageId == notification.MessageId)
            .Select(x => new { x.Status, x.ErrorCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (message is not null && message.Status != WhatsAppDeliveryStatus.Accepted)
            notification.ApplyDeliveryStatus(message.Status, message.ErrorCode, now);
    }

    private async Task<WhatsAppNotificationStatus> FinishAsync(WhatsAppNotification notification, WhatsAppNotificationStatus status,
        string code, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (status == WhatsAppNotificationStatus.Skipped) notification.MarkSkipped(code, now);
        else notification.MarkFailed(code, now);
        await db.SaveChangesAsync(cancellationToken);
        Log(notification, status == WhatsAppNotificationStatus.Skipped ? "SKIPPED" : "FAILED");
        return status;
    }

    private static TimeSpan MaxAge(WhatsAppNotificationType type, WhatsAppNotificationOptions settings) =>
        type is WhatsAppNotificationType.ClientCheckedIn or WhatsAppNotificationType.ProfessionalDelayed
            ? TimeSpan.FromMinutes(settings.OperationalMaxAgeMinutes)
            : TimeSpan.FromHours(settings.SchedulingMaxAgeHours);

    // Identifiers and codes only: never a phone, a name or template parameters.
    private void Log(WhatsAppNotification notification, string outcome) =>
        logger.LogInformation(
            "WhatsApp notification processed. Outcome: {Outcome}; NotificationId: {NotificationId}; Type: {Type}; Recipient: {Recipient}; ReservationId: {ReservationId}; VisitId: {VisitId}; Attempt: {Attempt}; MessageId: {MessageId}; Code: {Code}",
            outcome, notification.Id, notification.Type, notification.Recipient, notification.ReservationId, notification.VisitId,
            notification.Attempts, notification.MessageId, notification.LastErrorCode);
}
