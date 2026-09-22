using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GestaoPredio.Infrastructure.Notifications;

public sealed record WhatsAppDispatchSummary(int NoticesQueued, int Claimed, int Accepted, int Retried, int Failed, int Skipped,
    int Unconfirmed = 0);

/// <summary>
/// One dispatch cycle: queue due delay notices, settle interrupted sends, then claim and send due notifications one at
/// a time. Safe on several instances at once:
/// <list type="bullet">
/// <item>a notification is claimed by a single UPDATE … FOR UPDATE SKIP LOCKED, only when its turn comes, so its
/// lease covers one notification and never a whole batch;</item>
/// <item>every later write is conditional on the row version (xmin), so a dispatcher whose lease was taken over can
/// no longer write, and in particular cannot move the row to SENDING and call Meta;</item>
/// <item>SENDING is committed before the Cloud API call. From then on the attempt is never repeated: if its lease
/// expires, or the answer is a timeout or a dropped connection, Meta may already have the message, so it becomes
/// UNCONFIRMED and waits for webhook evidence (the echoed biz_opaque_callback_data) instead of being resent.</item>
/// </list>
/// Only failures that prove nothing reached the client (Meta answered with an error, or no connection was made) are
/// retried. The unique idempotency key stops a second notification for the same event.
/// </summary>
public sealed class WhatsAppNotificationDispatcher(
    ApplicationDbContext db,
    WhatsAppNotificationComposer composer,
    IWhatsAppService whatsApp,
    IOptionsMonitor<WhatsAppNotificationOptions> options,
    IOptionsMonitor<WhatsAppTemplateOptions> templates,
    TimeProvider timeProvider,
    ILogger<WhatsAppNotificationDispatcher> logger)
{
    /// <summary>Nothing reached the client: Meta answered with an error, or the connection was never made.</summary>
    private static readonly HashSet<string> RetryableFailures =
    [
        WhatsAppFailureCodes.ProviderUnavailable,
        WhatsAppFailureCodes.NetworkError,
        WhatsAppFailureCodes.RateLimited
    ];

    /// <summary>The request may have reached Meta: resending could deliver a second message.</summary>
    private static readonly HashSet<string> UnknownOutcomes =
    [
        WhatsAppFailureCodes.Timeout,
        WhatsAppFailureCodes.OutcomeUnknown,
        WhatsAppFailureCodes.InvalidResponse
    ];

    public async Task<WhatsAppDispatchSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var queued = await QueueDelayNoticesAsync(cancellationToken) + await QueueRemindersAsync(cancellationToken);
        var (interrupted, undecided) = await SettleInterruptedSendsAsync(cancellationToken);
        int claimed = 0, accepted = 0, retried = 0, failed = undecided, skipped = 0, unconfirmed = interrupted;
        while (claimed < options.CurrentValue.BatchSize)
        {
            var ids = await ClaimNextAsync(cancellationToken);
            if (ids.Count == 0) break;
            // Normally one id. Every claimed row is processed anyway, so none can be left locked until its lease expires.
            foreach (var id in ids)
            {
                claimed++;
                switch (await ProcessAsync(id, cancellationToken))
                {
                    case WhatsAppNotificationStatus.Accepted: accepted++; break;
                    case WhatsAppNotificationStatus.Pending: retried++; break;
                    case WhatsAppNotificationStatus.Failed: failed++; break;
                    case WhatsAppNotificationStatus.Skipped: skipped++; break;
                    case WhatsAppNotificationStatus.Unconfirmed: unconfirmed++; break;
                }
            }
        }
        return new WhatsAppDispatchSummary(queued, claimed, accepted, retried, failed, skipped, unconfirmed);
    }

    /// <summary>
    /// Claims and processes exactly one notification, through the same claim, composition, send and fencing as
    /// <see cref="RunOnceAsync"/> — for the administrative single-notice test while the worker stays disabled. Nothing
    /// else in the queue is touched: no delay scan, no sweep, no other notification. Null when that notification is not
    /// due (already processed, locked by a dispatcher, or waiting for a retry).
    /// </summary>
    public async Task<WhatsAppNotificationStatus?> DispatchOneAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var ids = await ClaimNextAsync(cancellationToken, notificationId);
        return ids.Count == 1 && ids[0] == notificationId ? await ProcessAsync(notificationId, cancellationToken) : null;
    }

    /// <summary>
    /// PROFESSIONAL_DELAYED policy: the client has checked in (visit WAITING) for an approved appointment whose start
    /// is at least <c>DelayFirstNoticeMinutes</c> in the past. Step n becomes due at first + n × repeat; only the
    /// latest due step is queued (a late scan never sends a burst of catch-up notices), at most
    /// <c>DelayMaxNotices</c> steps exist, and the key DELAY:{reservation}:{step} makes each step single-use.
    /// Times are instants (UTC), so the result does not depend on the server's local time zone.
    /// </summary>
    public async Task<int> QueueDelayNoticesAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (settings.DelayMaxNotices == 0) return 0;
        var now = timeProvider.GetUtcNow();
        var first = TimeSpan.FromMinutes(settings.DelayFirstNoticeMinutes);
        var repeat = TimeSpan.FromMinutes(settings.DelayRepeatMinutes);
        var latestStart = now - first;
        var earliestStart = now - TimeSpan.FromMinutes(settings.DelayLookbackMinutes);

        var late = await db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Approved && r.Kind != ReservationKind.Cancellation &&
                        r.CustomerId != null && r.StartAt <= latestStart && r.StartAt >= earliestStart &&
                        db.Visits.Any(v => v.ReservationId == r.Id && v.Status == VisitStatus.Waiting))
            .ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var reservation in late)
        {
            var step = (int)Math.Floor((now - reservation.StartAt - first) / repeat);
            if (step >= settings.DelayMaxNotices) continue;
            var key = WhatsAppNotification.DelayKey(reservation.Id, step);
            if (await db.WhatsAppNotifications.AnyAsync(x => x.IdempotencyKey == key, cancellationToken)) continue;
            db.WhatsAppNotifications.Add(WhatsAppNotification.ProfessionalDelayed(reservation, step, now));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                queued++;
                logger.LogInformation("WhatsApp notification queued. Type: {Type}; ReservationId: {ReservationId}; Step: {Step}",
                    WhatsAppNotificationType.ProfessionalDelayed, reservation.Id, step);
            }
            catch (DbUpdateException exception) when (IsDuplicateKey(exception))
            {
                // Another dispatcher queued the same step first: that is the idempotency guarantee working.
                db.ChangeTracker.Clear();
            }
        }
        return queued;
    }

    /// <summary>
    /// APPOINTMENT_REMINDER policy: an approved appointment with a customer whose start is still ahead and no more
    /// than <c>ReminderLeadHours</c> away. The REMINDER:{reservation} key makes it one per appointment, so a scan
    /// running every poll queues nothing new, and a reservation created inside the window is reminded at once. A
    /// cancellation after the row is queued is caught by the composer, which skips a no-longer-blocking reservation.
    /// </summary>
    public async Task<int> QueueRemindersAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        // No approved template means nothing could be sent: queueing would only pile up TEMPLATE_NOT_CONFIGURED rows,
        // so an environment turns reminders on simply by configuring Whatsapp:Templates:AppointmentReminder.
        if (settings.ReminderLeadHours == 0 ||
            templates.CurrentValue.NameFor(WhatsAppNotificationType.AppointmentReminder).Length == 0) return 0;
        var now = timeProvider.GetUtcNow();
        var horizon = now + TimeSpan.FromHours(settings.ReminderLeadHours);

        var due = await db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Approved && r.Kind != ReservationKind.Cancellation &&
                        r.CustomerId != null && r.StartAt > now && r.StartAt <= horizon)
            .OrderBy(r => r.StartAt).Take(settings.BatchSize).ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var reservation in due)
        {
            var key = WhatsAppNotification.ReminderKey(reservation.Id);
            if (await db.WhatsAppNotifications.AnyAsync(x => x.IdempotencyKey == key, cancellationToken)) continue;
            if (WhatsAppNotification.AppointmentReminder(reservation, now) is not { } notice) continue;
            db.WhatsAppNotifications.Add(notice);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                queued++;
                logger.LogInformation("WhatsApp notification queued. Type: {Type}; ReservationId: {ReservationId}",
                    WhatsAppNotificationType.AppointmentReminder, reservation.Id);
            }
            catch (DbUpdateException exception) when (IsDuplicateKey(exception))
            {
                // Another dispatcher queued the same reminder first: that is the idempotency guarantee working.
                db.ChangeTracker.Clear();
            }
        }
        return queued;
    }

    /// <summary>
    /// SENDING past its lease: the process died or stalled inside the Cloud API call, so the outcome is unknown and the
    /// row becomes UNCONFIRMED (never PENDING). UNCONFIRMED past its window without webhook evidence is failed with
    /// the explicit WHATSAPP_OUTCOME_UNKNOWN, visible to the admin, instead of risking a second message.
    /// </summary>
    private async Task<(int Interrupted, int Undecided)> SettleInterruptedSendsAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var stalled = await db.WhatsAppNotifications.AsNoTracking()
            .Where(x => x.Status == WhatsAppNotificationStatus.Sending && x.LockedUntil < now)
            .OrderBy(x => x.LockedUntil).Select(x => x.Id).Take(settings.BatchSize).ToListAsync(cancellationToken);
        var interrupted = 0;
        foreach (var id in stalled)
            if (await ChangeAsync(id, null, n => n.Status == WhatsAppNotificationStatus.Sending && n.LockedUntil < now
                    ? Park(n, WhatsAppNotificationCodes.DispatchInterrupted, now, settings)
                    : null, cancellationToken) is WhatsAppNotificationStatus.Unconfirmed)
                interrupted++;

        var overdue = await db.WhatsAppNotifications.AsNoTracking()
            .Where(x => x.Status == WhatsAppNotificationStatus.Unconfirmed && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt).Select(x => x.Id).Take(settings.BatchSize).ToListAsync(cancellationToken);
        var undecided = 0;
        foreach (var id in overdue)
            if (await ChangeAsync(id, null, n =>
                {
                    if (n.Status != WhatsAppNotificationStatus.Unconfirmed || n.NextAttemptAt > now) return null;
                    n.MarkFailed(WhatsAppFailureCodes.OutcomeUnknown, now);
                    return WhatsAppNotificationStatus.Failed;
                }, cancellationToken) is WhatsAppNotificationStatus.Failed)
                undecided++;
        return (interrupted, undecided);
    }

    /// <summary>Claims the single most overdue notification (or only <paramref name="onlyId"/>, when given), or none.</summary>
    private async Task<List<Guid>> ClaimNextAsync(CancellationToken cancellationToken, Guid? onlyId = null)
    {
        // Two non-null parameters rather than a nullable one, so Npgsql always knows their types.
        var anyId = onlyId is null;
        var id = onlyId ?? Guid.Empty;
        var settings = options.CurrentValue;
        // UTC, truncated to PostgreSQL's microsecond precision like every stored timestamp.
        var utc = timeProvider.GetUtcNow().ToUniversalTime();
        var now = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        var lockedUntil = now.AddSeconds(settings.LeaseSeconds);
        // Mirrors WhatsAppNotification.Claim: PENDING and due, or PROCESSING with an expired lease (a dispatcher died
        // before its send started, so nothing was sent). SENDING is never claimed: see SettleInterruptedSendsAsync.
        // The candidate is picked in a MATERIALIZED CTE, evaluated exactly once. "WHERE Id IN (SELECT … LIMIT 1 FOR
        // UPDATE SKIP LOCKED)" is not safe: PostgreSQL may re-run that subquery, and under concurrency one UPDATE then
        // claimed several rows (observed in the two-dispatcher test).
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH candidate AS MATERIALIZED (
                SELECT c."Id" FROM "WhatsAppNotifications" AS c
                WHERE ((c."Status" = 'PENDING' AND c."NextAttemptAt" <= {now})
                    OR (c."Status" = 'PROCESSING' AND c."LockedUntil" < {now}))
                  AND ({anyId} OR c."Id" = {id})
                ORDER BY c."NextAttemptAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            UPDATE "WhatsAppNotifications" AS n
            SET "Status" = 'PROCESSING', "LockedUntil" = {lockedUntil}, "Attempts" = n."Attempts" + 1, "UpdatedAt" = {now}
            FROM candidate
            WHERE n."Id" = candidate."Id"
            RETURNING n."Id" AS "Value"
            """).ToListAsync(cancellationToken);
        return ids;
    }

    private async Task<WhatsAppNotificationStatus?> ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var notification = await db.WhatsAppNotifications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (notification is null || notification.Status != WhatsAppNotificationStatus.Processing) return null;
        var attempt = notification.Attempts;
        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var sendStarted = false;
        string? acceptedMessageId = null;
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

            // Point of no return, committed together with a staged reschedule link. A conflict means this claim was
            // taken over (nothing is sent) or the customer used the link meanwhile (retry, then skip as obsolete).
            notification.BeginSend(now, now.AddSeconds(settings.SendLeaseSeconds));
            if (!await TrySaveAsync(cancellationToken))
                return await ChangeAsync(id, attempt, n => n.Status == WhatsAppNotificationStatus.Processing
                    ? Retry(n, WhatsAppNotificationCodes.DispatchError, now, settings)
                    : null, cancellationToken);

            sendStarted = true;
            var result = await whatsApp.SendTemplateAsync(composition.Phone!, composition.Template!, notification.CallbackData,
                cancellationToken);
            now = timeProvider.GetUtcNow();
            if (result is { Success: true, MessageId: { } messageId })
            {
                acceptedMessageId = messageId;
                return await AcceptAsync(id, attempt, messageId, now, cancellationToken);
            }

            var code = result.FailureCode ?? WhatsAppFailureCodes.RequestRejected;
            return await ChangeAsync(id, attempt, n =>
            {
                if (n.Status != WhatsAppNotificationStatus.Sending) return null;   // settled meanwhile
                if (UnknownOutcomes.Contains(code)) return Park(n, code, now, settings);
                if (RetryableFailures.Contains(code)) return Retry(n, code, now, settings);
                n.MarkFailed(code, now);
                return WhatsAppNotificationStatus.Failed;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "WhatsApp notification dispatch error. NotificationId: {NotificationId}; Type: {Type}; Attempt: {Attempt}; SendStarted: {SendStarted}",
                id, notification.Type, attempt, sendStarted);
            try
            {
                // Accepted: keep the wamid. Send started: Meta may have it, so never retry. Otherwise nothing left.
                if (acceptedMessageId is { } messageId)
                    return await AcceptAsync(id, attempt, messageId, timeProvider.GetUtcNow(), cancellationToken);
                var at = timeProvider.GetUtcNow();
                return await ChangeAsync(id, attempt, n => n.Status switch
                {
                    WhatsAppNotificationStatus.Sending when sendStarted => Park(n, WhatsAppNotificationCodes.DispatchError, at, settings),
                    WhatsAppNotificationStatus.Processing when !sendStarted => Retry(n, WhatsAppNotificationCodes.DispatchError, at, settings),
                    _ => null
                }, cancellationToken);
            }
            catch (Exception inner) when (inner is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The lease expires: PROCESSING is reclaimed, SENDING becomes UNCONFIRMED. Nothing is ever lost.
                logger.LogWarning(inner, "WhatsApp notification could not be settled; its lease will expire. NotificationId: {NotificationId}", id);
            }
            return null;
        }
    }

    /// <summary>
    /// Records the wamid on the attempt that sent it, even if the row became UNCONFIRMED meanwhile (lease expired while
    /// waiting for Meta) or the webhook already attached it through the callback data.
    /// </summary>
    private Task<WhatsAppNotificationStatus?> AcceptAsync(Guid id, int attempt, string messageId, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, attempt, async n =>
        {
            if (n.MessageId == messageId) return WhatsAppNotificationStatus.Accepted;
            if (n.Status is not (WhatsAppNotificationStatus.Sending or WhatsAppNotificationStatus.Unconfirmed)) return null;
            n.MarkAccepted(messageId, now);
            await CatchUpDeliveryStatusAsync(n, now, cancellationToken);
            return n.Status is WhatsAppNotificationStatus.Failed ? WhatsAppNotificationStatus.Failed : WhatsAppNotificationStatus.Accepted;
        }, cancellationToken);

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

    /// <summary>A final outcome decided before any send: written only if this dispatcher still owns the claim.</summary>
    private async Task<WhatsAppNotificationStatus?> FinishAsync(WhatsAppNotification notification, WhatsAppNotificationStatus status,
        string code, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (status == WhatsAppNotificationStatus.Skipped) notification.MarkSkipped(code, now);
        else notification.MarkFailed(code, now);
        if (!await TrySaveAsync(cancellationToken)) return null;
        Log(notification, status == WhatsAppNotificationStatus.Skipped ? "SKIPPED" : "FAILED");
        return status;
    }

    private static WhatsAppNotificationStatus Park(WhatsAppNotification notification, string code, DateTimeOffset now,
        WhatsAppNotificationOptions settings)
    {
        notification.MarkUnconfirmed(code, now.AddMinutes(settings.UnconfirmedWindowMinutes), now);
        return WhatsAppNotificationStatus.Unconfirmed;
    }

    private static WhatsAppNotificationStatus Retry(WhatsAppNotification notification, string code, DateTimeOffset now,
        WhatsAppNotificationOptions settings)
    {
        if (notification.Attempts >= settings.MaxAttempts)
        {
            notification.MarkFailed(code, now);
            return WhatsAppNotificationStatus.Failed;
        }
        notification.ScheduleRetry(code, now + settings.RetryDelayBefore(notification.Attempts + 1), now);
        return WhatsAppNotificationStatus.Pending;
    }

    private Task<WhatsAppNotificationStatus?> ChangeAsync(Guid id, int? attempt,
        Func<WhatsAppNotification, WhatsAppNotificationStatus?> change, CancellationToken cancellationToken) =>
        ChangeAsync(id, attempt, n => Task.FromResult(change(n)), cancellationToken);

    /// <summary>
    /// Applies <paramref name="change"/> to the current row and saves it conditionally on its row version, re-reading
    /// on a conflict (a webhook or a sweep touched it). With <paramref name="attempt"/>, the row must still belong to
    /// that claim; a null result means nothing was written.
    /// </summary>
    private async Task<WhatsAppNotificationStatus?> ChangeAsync(Guid id, int? attempt,
        Func<WhatsAppNotification, Task<WhatsAppNotificationStatus?>> change, CancellationToken cancellationToken)
    {
        for (var round = 0; round < 3; round++)
        {
            db.ChangeTracker.Clear();
            var current = await db.WhatsAppNotifications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (current is null || (attempt is { } owned && current.Attempts != owned)) return null;
            if (await change(current) is not { } status) return null;
            if (!await TrySaveAsync(cancellationToken)) continue;
            Log(current, StatusStorage(status));
            return status;
        }
        return null;
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static string StatusStorage(WhatsAppNotificationStatus status) =>
        status == WhatsAppNotificationStatus.Pending ? "RETRY_SCHEDULED" : WhatsAppNotificationConfiguration.StatusStorage[status];

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

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        (postgres.ConstraintName is null ||
         postgres.ConstraintName.Contains(WhatsAppNotificationConfiguration.IdempotencyIndex, StringComparison.Ordinal));
}
