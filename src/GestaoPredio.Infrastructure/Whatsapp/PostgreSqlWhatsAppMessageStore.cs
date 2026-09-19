using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GestaoPredio.Infrastructure.Whatsapp;

/// <summary>
/// EF Core implementation of the message store. Idempotency rests on the unique index over MessageId plus the
/// forward-only transition rules in <see cref="WhatsAppMessage"/>, so a replay or a concurrent first write from
/// another node cannot duplicate a row or move a status backwards.
/// </summary>
public sealed class PostgreSqlWhatsAppMessageStore(
    ApplicationDbContext db,
    TimeProvider timeProvider,
    ILogger<PostgreSqlWhatsAppMessageStore> logger) : IWhatsAppMessageStore
{
    private const string UniqueMessageIdIndex = "UX_WhatsAppMessages_MessageId";

    public async Task RecordAcceptedAsync(string messageId, string recipientPhone, string? phoneNumberId,
        WhatsAppMessageType messageType, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(messageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ReconcileAccepted(recipientPhone, phoneNumberId, messageType, occurredAt))
                await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.WhatsAppMessages.Add(WhatsAppMessage.CreateAccepted(messageId, recipientPhone, phoneNumberId, occurredAt, messageType));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateMessageId(exception))
        {
            // Another writer (webhook, retry, second instance) inserted the same wamid first.
            Detach();
            var winner = await FindAsync(messageId, cancellationToken);
            if (winner is null) throw;
            if (winner.ReconcileAccepted(recipientPhone, phoneNumberId, messageType, occurredAt))
                await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ApplyStatusAsync(WhatsAppStatusUpdate update, CancellationToken cancellationToken)
    {
        if (update.Status == WhatsAppDeliveryStatus.Unknown)
        {
            logger.LogInformation("WhatsApp status ignored. MessageId: {MessageId}; Reason: UNKNOWN_STATUS", update.MessageId);
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var messageChanged = await ApplyOnMessageAsync(update, now, cancellationToken);
        // Always, even for a replayed status: an earlier delivery may have updated the message but lost the race on
        // the notification, and Meta redelivers until it gets a 200.
        var notificationChanged = await MirrorOnNotificationsAsync(update, now, cancellationToken);
        return messageChanged || notificationChanged;
    }

    private async Task<bool> ApplyOnMessageAsync(WhatsAppStatusUpdate update, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(update.MessageId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.ApplyStatus(update.Status, update.ReportedAt, update.ErrorCode, update.ErrorTitle,
                    update.ErrorDetails, now))
                return false;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        db.WhatsAppMessages.Add(WhatsAppMessage.CreateFromStatus(update.MessageId, update.Status, update.RecipientId,
            update.PhoneNumberId, update.WhatsAppBusinessAccountId, update.ReportedAt, update.ErrorCode,
            update.ErrorTitle, update.ErrorDetails, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateMessageId(exception))
        {
            Detach();
            var winner = await FindAsync(update.MessageId, cancellationToken);
            if (winner is null) throw;
            if (!winner.ApplyStatus(update.Status, update.ReportedAt, update.ErrorCode, update.ErrorTitle,
                    update.ErrorDetails, now))
                return false;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    /// <summary>
    /// Carries the status onto the operational notification that sent the message. It is found by wamid or, when the
    /// dispatcher never learned the wamid (timeout, dropped connection, crash mid-call), by the echoed
    /// biz_opaque_callback_data, which attaches the wamid to that notification. The notification applies its own rules:
    /// forward-only, and a known wamid is never replaced. Saved on its own, conditionally on the notification's row
    /// version, and re-read on a conflict with the dispatcher.
    /// </summary>
    private async Task<bool> MirrorOnNotificationsAsync(WhatsAppStatusUpdate update, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var round = 0; round < 3; round++)
        {
            db.ChangeTracker.Clear();
            var notifications = await db.WhatsAppNotifications.Where(x => x.MessageId == update.MessageId).ToListAsync(cancellationToken);
            var changed = false;
            if (notifications.Count == 0 &&
                GestaoPredio.Domain.Notifications.WhatsAppNotification.TryParseCallbackData(update.CallbackData, out var notificationId) &&
                await db.WhatsAppNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken) is { } sender)
            {
                changed = sender.AttachMessageId(update.MessageId, now);
                if (changed)
                {
                    logger.LogInformation("WhatsApp notification matched by callback data. NotificationId: {NotificationId}; MessageId: {MessageId}",
                        sender.Id, update.MessageId);
                    notifications.Add(sender);
                }
            }
            foreach (var notification in notifications)
                changed |= notification.ApplyDeliveryStatus(update.Status, update.ErrorCode, now);
            if (!changed) return false;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The dispatcher wrote the same notification meanwhile: re-read and apply again.
            }
        }
        return false;
    }

    private Task<WhatsAppMessage?> FindAsync(string messageId, CancellationToken cancellationToken) =>
        db.WhatsAppMessages.SingleOrDefaultAsync(x => x.MessageId == messageId, cancellationToken);

    private void Detach()
    {
        foreach (var entry in db.ChangeTracker.Entries<WhatsAppMessage>().ToArray())
            entry.State = EntityState.Detached;
    }

    private static bool IsDuplicateMessageId(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        (postgres.ConstraintName is null || postgres.ConstraintName.Contains(UniqueMessageIdIndex, StringComparison.Ordinal));
}
