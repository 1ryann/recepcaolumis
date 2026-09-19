using System.Globalization;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
/// Each notification type contributes its own rendering; a type without one is skipped as NOT_IMPLEMENTED.
/// </summary>
public sealed class WhatsAppNotificationComposer(
    ApplicationDbContext db,
    IOptionsMonitor<WhatsAppTemplateOptions> templates,
    IOptionsMonitor<WhatsAppNotificationOptions> options,
    TimeZoneInfo timeZone)
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<WhatsAppComposition> ComposeAsync(WhatsAppNotification notification, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (notification.Type == WhatsAppNotificationType.AppointmentReminder)
            return WhatsAppComposition.Skip(WhatsAppNotificationCodes.NotImplemented);
        var templateName = templates.CurrentValue.NameFor(notification.Type);
        if (templateName.Length == 0)
            return WhatsAppComposition.Skip(WhatsAppNotificationCodes.TemplateNotConfigured);

        return notification.Type switch
        {
            WhatsAppNotificationType.ClientCheckedIn => await ClientCheckedInAsync(notification, templateName, cancellationToken),
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

    private Task<Professional?> ProfessionalAsync(Guid professionalId, CancellationToken cancellationToken) =>
        db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == professionalId && x.IsActive, cancellationToken);

    private WhatsAppTemplate Template(string name, IReadOnlyList<string> parameters, string? urlButton = null) =>
        new(name, options.CurrentValue.LanguageCode, parameters, urlButton);

    private string Time(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString("HH:mm", Brazil);

    /// <summary>Only the first name leaves the system, capped like the previous notification bodies.</summary>
    public static string FirstName(string? value)
    {
        var first = value?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "cliente" : first.Length > 80 ? first[..80] : first;
    }
}
