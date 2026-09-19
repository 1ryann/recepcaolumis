using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Notifications;
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
public sealed class WhatsAppNotificationComposer(IOptionsMonitor<WhatsAppTemplateOptions> templates)
{
    public Task<WhatsAppComposition> ComposeAsync(WhatsAppNotification notification, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (notification.Type == WhatsAppNotificationType.AppointmentReminder)
            return Task.FromResult(WhatsAppComposition.Skip(WhatsAppNotificationCodes.NotImplemented));
        var templateName = templates.CurrentValue.NameFor(notification.Type);
        if (templateName.Length == 0)
            return Task.FromResult(WhatsAppComposition.Skip(WhatsAppNotificationCodes.TemplateNotConfigured));

        return Task.FromResult(WhatsAppComposition.Skip(WhatsAppNotificationCodes.NotImplemented));
    }

    /// <summary>Only the first name leaves the system, capped like the previous notification bodies.</summary>
    public static string FirstName(string? value)
    {
        var first = value?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "cliente" : first.Length > 80 ? first[..80] : first;
    }
}
