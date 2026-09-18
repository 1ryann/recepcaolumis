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
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(messageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ReconcileAccepted(recipientPhone, phoneNumberId, WhatsAppMessageType.Text, occurredAt))
                await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.WhatsAppMessages.Add(WhatsAppMessage.CreateAccepted(messageId, recipientPhone, phoneNumberId, occurredAt));
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
            if (winner.ReconcileAccepted(recipientPhone, phoneNumberId, WhatsAppMessageType.Text, occurredAt))
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
