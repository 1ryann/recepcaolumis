namespace GestaoPredio.Application.Notifications;

public static class NotificationEventTypes
{
    public const string ProfessionalVisitWaiting = "PROFESSIONAL_VISIT_WAITING";
}

public sealed record ProfessionalNotificationEvent(
    Guid ProfessionalId,
    string EventType,
    string VisitorName,
    DateTimeOffset ArrivedAt,
    Guid? ReservationId);

public sealed record NotificationMessage(
    Guid ProfessionalId,
    string DestinationPhone,
    string EventType,
    string Body);

public sealed record NotificationResult(bool Success, string Provider, string? FailureCode)
{
    public static NotificationResult Succeeded(string provider) => new(true, provider, null);
    public static NotificationResult Failed(string provider, string failureCode) => new(false, provider, failureCode);
}

public interface INotificationService
{
    Task<NotificationResult> NotifyProfessionalAsync(
        ProfessionalNotificationEvent notification,
        CancellationToken cancellationToken);
}

public interface INotificationProvider
{
    string Name { get; }

    Task<NotificationResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken);
}
