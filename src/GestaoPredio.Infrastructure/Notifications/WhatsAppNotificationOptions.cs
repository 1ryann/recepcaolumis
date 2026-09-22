using System.Text.RegularExpressions;
using GestaoPredio.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Notifications;

/// <summary>
/// Dispatcher behaviour (section "Whatsapp:Notifications"). <see cref="Enabled"/> defaults to false: business events
/// are always recorded, but nothing is sent until an operator turns dispatch on for that environment.
/// </summary>
public sealed class WhatsAppNotificationOptions
{
    public const string SectionName = "Whatsapp:Notifications";
    public static readonly int[] DefaultRetryDelaysSeconds = [30, 120, 600];

    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 20;
    /// <summary>
    /// Lease of one claimed notification while it is prepared (checks, recipient, reschedule link). Notifications are
    /// claimed one at a time, so this never has to cover a whole batch.
    /// </summary>
    public int LeaseSeconds { get; set; } = 120;
    /// <summary>
    /// Lease taken when the Cloud API call starts. It must outlast the longest possible call (Whatsapp:TimeoutSeconds
    /// is at most 60), so the minimum is 70. If it still expires, the send is treated as an unknown outcome, never resent.
    /// </summary>
    public int SendLeaseSeconds { get; set; } = 90;
    /// <summary>How long an unknown send outcome waits for webhook evidence before it is failed as WHATSAPP_OUTCOME_UNKNOWN.</summary>
    public int UnconfirmedWindowMinutes { get; set; } = 15;
    /// <summary>Total send attempts, the first one included.</summary>
    public int MaxAttempts { get; set; } = 4;
    /// <summary>Wait before attempt 2, 3, …; the last value repeats. Null means the defaults (30s, 2min, 10min).</summary>
    public int[]? RetryDelaysSeconds { get; set; }
    public string LanguageCode { get; set; } = "pt_BR";

    /// <summary>First PROFESSIONAL_DELAYED notice once the appointment is this late with the client waiting.</summary>
    public int DelayFirstNoticeMinutes { get; set; } = 10;
    /// <summary>Minimum gap between two delay notices for the same appointment.</summary>
    public int DelayRepeatMinutes { get; set; } = 15;
    /// <summary>At most this many delay notices per appointment.</summary>
    public int DelayMaxNotices { get; set; } = 2;
    /// <summary>Appointments that started longer ago than this are no longer scanned for delays.</summary>
    public int DelayLookbackMinutes { get; set; } = 180;

    /// <summary>
    /// How far ahead of an appointment its APPOINTMENT_REMINDER is queued. 0 turns reminders off, which is what an
    /// environment without the approved template should use so nothing piles up as TEMPLATE_NOT_CONFIGURED.
    /// </summary>
    public int ReminderLeadHours { get; set; } = 24;

    /// <summary>Check-in and delay notices are pointless once this old; they are skipped as EXPIRED.</summary>
    public int OperationalMaxAgeMinutes { get; set; } = 30;
    /// <summary>Confirmation, reschedule and cancellation notices expire after this many hours.</summary>
    public int SchedulingMaxAgeHours { get; set; } = 24;

    public TimeSpan RetryDelayBefore(int nextAttempt)
    {
        var delays = RetryDelaysSeconds is { Length: > 0 } configured ? configured : DefaultRetryDelaysSeconds;
        var index = Math.Clamp(nextAttempt - 2, 0, delays.Length - 1);
        return TimeSpan.FromSeconds(delays[index]);
    }
}

/// <summary>
/// Approved WhatsApp Business template names per notification type (section "Whatsapp:Templates"). A type whose
/// template is empty is recorded but skipped as TEMPLATE_NOT_CONFIGURED, never sent as free text.
/// </summary>
public sealed class WhatsAppTemplateOptions
{
    public const string SectionName = "Whatsapp:Templates";

    public string ClientCheckedIn { get; set; } = "";
    public string ProfessionalDelayed { get; set; } = "";
    public string ProfessionalCancelled { get; set; } = "";
    public string AppointmentCancelled { get; set; } = "";
    public string AppointmentRescheduled { get; set; } = "";
    public string AppointmentConfirmed { get; set; } = "";
    public string AppointmentReminder { get; set; } = "";

    public string NameFor(WhatsAppNotificationType type) => (type switch
    {
        WhatsAppNotificationType.ClientCheckedIn => ClientCheckedIn,
        WhatsAppNotificationType.ProfessionalDelayed => ProfessionalDelayed,
        WhatsAppNotificationType.ProfessionalCancelled => ProfessionalCancelled,
        WhatsAppNotificationType.AppointmentCancelled => AppointmentCancelled,
        WhatsAppNotificationType.AppointmentRescheduled => AppointmentRescheduled,
        WhatsAppNotificationType.AppointmentConfirmed => AppointmentConfirmed,
        WhatsAppNotificationType.AppointmentReminder => AppointmentReminder,
        _ => ""
    } ?? "").Trim();
}

/// <summary>Messages name the key, never a value.</summary>
public sealed partial class WhatsAppNotificationOptionsValidator : IValidateOptions<WhatsAppNotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppNotificationOptions options)
    {
        var errors = new List<string>();
        if (options.PollIntervalSeconds is < 1 or > 3600) errors.Add("Whatsapp:Notifications:PollIntervalSeconds deve estar entre 1 e 3600.");
        if (options.BatchSize is < 1 or > 500) errors.Add("Whatsapp:Notifications:BatchSize deve estar entre 1 e 500.");
        if (options.LeaseSeconds is < 30 or > 3600) errors.Add("Whatsapp:Notifications:LeaseSeconds deve estar entre 30 e 3600.");
        if (options.SendLeaseSeconds is < 70 or > 3600) errors.Add("Whatsapp:Notifications:SendLeaseSeconds deve estar entre 70 e 3600.");
        if (options.UnconfirmedWindowMinutes is < 1 or > 1440) errors.Add("Whatsapp:Notifications:UnconfirmedWindowMinutes deve estar entre 1 e 1440.");
        if (options.MaxAttempts is < 1 or > 20) errors.Add("Whatsapp:Notifications:MaxAttempts deve estar entre 1 e 20.");
        if (options.RetryDelaysSeconds is { } delays && delays.Any(x => x is < 1 or > 86400))
            errors.Add("Whatsapp:Notifications:RetryDelaysSeconds deve conter valores entre 1 e 86400.");
        if (!LanguagePattern().IsMatch(options.LanguageCode ?? "")) errors.Add("Whatsapp:Notifications:LanguageCode é inválido.");
        if (options.DelayFirstNoticeMinutes is < 1 or > 240) errors.Add("Whatsapp:Notifications:DelayFirstNoticeMinutes deve estar entre 1 e 240.");
        if (options.DelayRepeatMinutes is < 1 or > 240) errors.Add("Whatsapp:Notifications:DelayRepeatMinutes deve estar entre 1 e 240.");
        if (options.DelayMaxNotices is < 0 or > 10) errors.Add("Whatsapp:Notifications:DelayMaxNotices deve estar entre 0 e 10.");
        if (options.DelayLookbackMinutes is < 1 or > 1440) errors.Add("Whatsapp:Notifications:DelayLookbackMinutes deve estar entre 1 e 1440.");
        if (options.ReminderLeadHours is < 0 or > 168) errors.Add("Whatsapp:Notifications:ReminderLeadHours deve estar entre 0 e 168.");
        if (options.OperationalMaxAgeMinutes is < 1 or > 1440) errors.Add("Whatsapp:Notifications:OperationalMaxAgeMinutes deve estar entre 1 e 1440.");
        if (options.SchedulingMaxAgeHours is < 1 or > 168) errors.Add("Whatsapp:Notifications:SchedulingMaxAgeHours deve estar entre 1 e 168.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    [GeneratedRegex("^[a-z]{2,3}(_[A-Z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
}
