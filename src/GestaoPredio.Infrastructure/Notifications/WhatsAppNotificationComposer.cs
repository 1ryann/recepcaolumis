using System.Globalization;
using System.Security.Cryptography;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Notifications;

/// <summary>Stable outcome codes stored on a notification. Never provider free text, names or phones.</summary>
public static class WhatsAppNotificationCodes
{
    public const string TemplateNotConfigured = "TEMPLATE_NOT_CONFIGURED";
    public const string Obsolete = "OBSOLETE";
    public const string Expired = "EXPIRED";
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string RecipientUnavailable = "RECIPIENT_UNAVAILABLE";
    public const string RecipientPhoneInvalid = "RECIPIENT_PHONE_INVALID";
    public const string MaxAttemptsExceeded = "MAX_ATTEMPTS_EXCEEDED";
    public const string DispatchError = "DISPATCH_ERROR";
    /// <summary>The dispatcher stopped inside the Cloud API call (crash, stall): the send outcome is unknown.</summary>
    public const string DispatchInterrupted = "DISPATCH_INTERRUPTED";
    /// <summary>The recipient never opted in to WhatsApp messages (docs/operations/whatsapp-consent.md).</summary>
    public const string RecipientNotOptedIn = "RECIPIENT_NOT_OPTED_IN";
    /// <summary>The recipient withdrew the opt-in.</summary>
    public const string RecipientOptedOut = "RECIPIENT_OPTED_OUT";
}

/// <summary>What the dispatcher should do with a claimed notification.</summary>
public sealed record WhatsAppComposition(string? Phone, WhatsAppTemplate? Template, string? SkipCode, string? FailCode)
{
    public static WhatsAppComposition Send(string phone, WhatsAppTemplate template) => new(phone, template, null, null);
    public static WhatsAppComposition Skip(string code) => new(null, null, code, null);
    public static WhatsAppComposition Fail(string code) => new(null, null, null, code);
}

/// <summary>
/// Turns a notification into a concrete template message at send time, reading the recipient, names and times
/// from the real records. Only operational data leaves the system: first names, the professional's name, date,
/// time, minutes late and (for a professional cancellation) the reschedule token. Nothing clinical, financial or
/// free-text (notes, reasons) is ever used. A notification whose business situation changed meanwhile (visit
/// already in service, reservation no longer approved, reschedule already used) is skipped as OBSOLETE.
/// </summary>
public sealed class WhatsAppNotificationComposer(
    ApplicationDbContext db,
    IOptionsMonitor<WhatsAppTemplateOptions> templates,
    IOptionsMonitor<WhatsAppNotificationOptions> options,
    IConfiguration configuration,
    TimeZoneInfo timeZone)
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<WhatsAppComposition> ComposeAsync(WhatsAppNotification notification, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var templateName = templates.CurrentValue.NameFor(notification.Type);
        if (templateName.Length == 0)
            return WhatsAppComposition.Skip(WhatsAppNotificationCodes.TemplateNotConfigured);

        return notification.Type switch
        {
            WhatsAppNotificationType.ClientCheckedIn => await ClientCheckedInAsync(notification, templateName, cancellationToken),
            WhatsAppNotificationType.ProfessionalDelayed => await ProfessionalDelayedAsync(notification, templateName, now, cancellationToken),
            WhatsAppNotificationType.ProfessionalCancelled or WhatsAppNotificationType.AppointmentCancelled =>
                await CancelledAsync(notification, templateName, now, cancellationToken),
            WhatsAppNotificationType.AppointmentRescheduled or WhatsAppNotificationType.AppointmentConfirmed
                or WhatsAppNotificationType.AppointmentReminder =>
                await ScheduledAsync(notification, templateName, now, cancellationToken),
            _ => WhatsAppComposition.Skip(WhatsAppNotificationCodes.NotImplemented)
        };
    }

    // "Olá, {{1}}. O cliente {{2}} já chegou para o atendimento das {{3}}." — to the professional.
    private async Task<WhatsAppComposition> ClientCheckedInAsync(WhatsAppNotification notification, string templateName,
        CancellationToken cancellationToken)
    {
        var visit = await db.Visits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.VisitId, cancellationToken);
        // Already being seen, finished or cancelled: the arrival notice would be noise.
        if (visit is null || visit.Status != VisitStatus.Waiting) return WhatsAppComposition.Skip(WhatsAppNotificationCodes.Obsolete);
        var professional = await ProfessionalAsync(visit.ProfessionalId, cancellationToken);
        if (professional is null) return WhatsAppComposition.Fail(WhatsAppNotificationCodes.RecipientUnavailable);
        if (WithoutOptIn(professional.WhatsAppOptIn) is { } noOptIn) return noOptIn;
        if (!WhatsAppNormalizer.TryNormalize(professional.WhatsApp, out var phone))
            return WhatsAppComposition.Fail(WhatsAppNotificationCodes.RecipientPhoneInvalid);

        var appointmentAt = visit.ArrivedAt;
        if (visit.ReservationId is { } reservationId &&
            await db.Reservations.AsNoTracking().Where(x => x.Id == reservationId).Select(x => (DateTimeOffset?)x.StartAt)
                .SingleOrDefaultAsync(cancellationToken) is { } startAt)
            appointmentAt = startAt;

        return WhatsAppComposition.Send(phone, Template(templateName,
            [professional.Name, FirstName(visit.VisitorName), Time(appointmentAt)]));
    }

    // "Olá, {{1}}. Seu atendimento com {{2}} está com um atraso de aproximadamente {{3}} minutos. Pedimos que aguarde."
    private async Task<WhatsAppComposition> ProfessionalDelayedAsync(WhatsAppNotification notification, string templateName,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reservation = await ReservationAsync(notification, cancellationToken);
        if (reservation is null || !reservation.BlocksResources) return WhatsAppComposition.Skip(WhatsAppNotificationCodes.Obsolete);
        // Only while the client is still waiting and the service has not started.
        if (!await db.Visits.AsNoTracking().AnyAsync(x => x.ReservationId == reservation.Id && x.Status == VisitStatus.Waiting,
                cancellationToken))
            return WhatsAppComposition.Skip(WhatsAppNotificationCodes.Obsolete);
        var (customer, failure) = await CustomerRecipientAsync(reservation.CustomerId, cancellationToken);
        if (failure is not null) return failure;
        var professionalName = await ProfessionalNameAsync(reservation.ProfessionalId, cancellationToken);
        var minutes = Math.Max(1, (int)Math.Floor((now - reservation.StartAt).TotalMinutes));

        return WhatsAppComposition.Send(customer!.Value.Phone, Template(templateName,
            [customer.Value.FirstName, professionalName, minutes.ToString(CultureInfo.InvariantCulture)]));
    }

    // PROFESSIONAL_CANCELLED: "Olá, {{1}}. Seu atendimento com {{2}}, previsto para {{3}} às {{4}}, foi cancelado. …"
    //   + URL button whose dynamic suffix is a fresh one-time reschedule token.
    // APPOINTMENT_CANCELLED: same body, no button ("Entre em contato com a recepção para reagendar.").
    private async Task<WhatsAppComposition> CancelledAsync(WhatsAppNotification notification, string templateName,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reservation = await ReservationAsync(notification, cancellationToken);
        if (reservation is null || reservation.Status != ReservationStatus.Cancelled)
            return WhatsAppComposition.Skip(WhatsAppNotificationCodes.Obsolete);
        var (customer, failure) = await CustomerRecipientAsync(reservation.CustomerId, cancellationToken);
        if (failure is not null) return failure;
        var professionalName = await ProfessionalNameAsync(reservation.ProfessionalId, cancellationToken);

        string? rescheduleToken = null;
        if (notification.Type == WhatsAppNotificationType.ProfessionalCancelled)
        {
            rescheduleToken = await StageRescheduleTokenAsync(notification.Id, reservation.Id, now, cancellationToken);
            // The client already rebooked through an earlier link (or it was revoked): nothing left to offer.
            if (rescheduleToken is null) return WhatsAppComposition.Skip(WhatsAppNotificationCodes.Obsolete);
        }

        return WhatsAppComposition.Send(customer!.Value.Phone, Template(templateName,
            [customer.Value.FirstName, professionalName, Date(reservation.StartAt), Time(reservation.StartAt)], rescheduleToken));
    }

    // "Olá, {{1}}. Seu atendimento com {{2}} foi reagendado para {{3}} às {{4}}." /
    // "Olá, {{1}}. Seu atendimento com {{2}} está confirmado para {{3}} às {{4}}." /
    // APPOINTMENT_REMINDER: same four parameters, as a reminder of the appointment ahead. An appointment cancelled
    // or already started between queueing and sending is skipped here as OBSOLETE.
    private async Task<WhatsAppComposition> ScheduledAsync(WhatsAppNotification notification, string templateName,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reservation = await ReservationAsync(notification, cancellationToken);
        if (reservation is null || !reservation.BlocksResources || reservation.StartAt <= now)
            return WhatsAppComposition.Skip(WhatsAppNotificationCodes.Obsolete);
        var (customer, failure) = await CustomerRecipientAsync(reservation.CustomerId, cancellationToken);
        if (failure is not null) return failure;
        var professionalName = await ProfessionalNameAsync(reservation.ProfessionalId, cancellationToken);

        return WhatsAppComposition.Send(customer!.Value.Phone, Template(templateName,
            [customer.Value.FirstName, professionalName, Date(reservation.StartAt), Time(reservation.StartAt)]));
    }

    /// <summary>
    /// Regenerates the reservation's reschedule token for this send (see docs/operations/whatsapp-notifications.md,
    /// "Decisão: token do link de reagendamento"). The raw token exists only in memory until it becomes the URL button
    /// suffix; the database keeps the hash, exactly as the incident flow does. The rotation and its audit entry are only
    /// staged here: the dispatcher commits them in the same SaveChanges that moves the notification to SENDING, BEFORE
    /// Meta is called. So the delivered link is already valid, and a dispatcher that lost its claim (row version)
    /// cannot rotate the link another dispatcher is sending. A used or revoked token is never rotated (Rotate would clear
    /// UsedAt/RevokedAt and revive it): null is returned and the notice is skipped. The token's row version turns a
    /// concurrent use by the customer into a conflict → retry → skip. Rotation happens only for an attempt that will
    /// call Meta; an unknown outcome is never retried, so a link that may have been delivered is never replaced.
    /// </summary>
    private async Task<string?> StageRescheduleTokenAsync(Guid notificationId, Guid reservationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromHours(Math.Max(1, configuration.GetValue("Rescheduling:LinkTtlHours", 48)));
        var raw = RandomNumberGenerator.GetBytes(32);
        var hash = SHA256.HashData(raw);
        var token = await db.RescheduleTokens.SingleOrDefaultAsync(x => x.ReservationId == reservationId, cancellationToken);
        if (token is null)
        {
            token = RescheduleToken.Create(reservationId, hash, now, now + ttl);
            db.RescheduleTokens.Add(token);
        }
        else if (token.UsedAt is not null || token.RevokedAt is not null)
            return null;
        else
            token.Rotate(hash, now, now + ttl);

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = AuditActions.RescheduleLinkIssued,
            Result = "SUCCEEDED",
            TargetEntityType = AuditTargetTypes.RescheduleToken,
            TargetEntityId = token.Id,
            OccurredAt = now,
            CorrelationId = $"whatsapp-notification:{notificationId}"
        });
        return WebEncoders.Base64UrlEncode(raw);
    }

    private Task<Reservation?> ReservationAsync(WhatsAppNotification notification, CancellationToken cancellationToken) =>
        db.Reservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.ReservationId, cancellationToken);

    private Task<Professional?> ProfessionalAsync(Guid professionalId, CancellationToken cancellationToken) =>
        db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == professionalId && x.IsActive, cancellationToken);

    private async Task<string> ProfessionalNameAsync(Guid professionalId, CancellationToken cancellationToken) =>
        await db.Professionals.AsNoTracking().Where(x => x.Id == professionalId).Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "seu profissional";

    private async Task<((string Phone, string FirstName)? Recipient, WhatsAppComposition? Failure)> CustomerRecipientAsync(
        Guid? customerId, CancellationToken cancellationToken)
    {
        var customer = customerId is null
            ? null
            : await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId && x.IsActive, cancellationToken);
        if (customer is null) return (null, WhatsAppComposition.Fail(WhatsAppNotificationCodes.RecipientUnavailable));
        if (WithoutOptIn(customer.WhatsAppOptIn) is { } noOptIn) return (null, noOptIn);
        if (!WhatsAppNormalizer.TryNormalize(customer.Phone, out var phone))
            return (null, WhatsAppComposition.Fail(WhatsAppNotificationCodes.RecipientPhoneInvalid));
        return ((phone, FirstName(customer.Name)), null);
    }

    /// <summary>
    /// Read at send time from the real record, so a withdrawal made after the event was queued is honoured. Checked
    /// before anything else is prepared: no reschedule link is issued for someone who will not receive it.
    /// </summary>
    private static WhatsAppComposition? WithoutOptIn(WhatsAppOptInState optIn) => optIn.Status switch
    {
        WhatsAppOptInStatus.Granted => null,
        WhatsAppOptInStatus.Revoked => WhatsAppComposition.Skip(WhatsAppNotificationCodes.RecipientOptedOut),
        _ => WhatsAppComposition.Skip(WhatsAppNotificationCodes.RecipientNotOptedIn)
    };

    private WhatsAppTemplate Template(string name, IReadOnlyList<string> parameters, string? urlButton = null) =>
        new(name, options.CurrentValue.LanguageCode, parameters, urlButton);

    private string Time(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString("HH:mm", Brazil);

    private string Date(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString("dd/MM/yyyy", Brazil);

    /// <summary>Only the first name leaves the system, capped like the previous notification bodies.</summary>
    public static string FirstName(string? value)
    {
        var first = value?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "cliente" : first.Length > 80 ? first[..80] : first;
    }
}
