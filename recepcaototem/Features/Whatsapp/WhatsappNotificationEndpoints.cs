using System.Security.Claims;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Notifications;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Whatsapp;

/// <summary>
/// One operational notification as the admin sees it: identifiers, status, attempts and a stable error code.
/// Deliberately no phone number, names, template parameters, reschedule link or credential of any kind.
/// </summary>
public sealed record WhatsappNotificationResponse(
    Guid Id,
    string Type,
    string Recipient,
    string Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? LastErrorCode,
    string? MessageId,
    Guid? ReservationId,
    Guid? VisitId,
    Guid? ProfessionalId,
    Guid? CustomerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Selects who receives the test (by the number already on their record) and which of the six notices.</summary>
public sealed record WhatsappTemplateTestRequest(string NotificationType, string Phone) : IStrictModuleRequest;

/// <summary>Outcome of the single test notice. No phone, name, parameter, link or credential.</summary>
public sealed record WhatsappTemplateTestResponse(Guid NotificationId, string NotificationType, string Status, string? Code,
    string? MessageId);

public static class WhatsappNotificationEndpoints
{
    public const string ListPath = "/api/admin/whatsapp/notifications";
    public const string TemplateTestPath = "/api/admin/whatsapp/template-test";
    public const string TemplateTestAuditAction = "WHATSAPP_TEMPLATE_TEST";

    /// <summary>The six implemented notices. APPOINTMENT_REMINDER is prepared but not implemented, so it cannot be tested.</summary>
    private static readonly HashSet<WhatsAppNotificationType> TestableTypes =
    [
        WhatsAppNotificationType.ClientCheckedIn, WhatsAppNotificationType.ProfessionalDelayed,
        WhatsAppNotificationType.ProfessionalCancelled, WhatsAppNotificationType.AppointmentCancelled,
        WhatsAppNotificationType.AppointmentRescheduled, WhatsAppNotificationType.AppointmentConfirmed
    ];

    public static IEndpointRouteBuilder MapWhatsappNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Read-only. Nothing here can send, resend, pick a recipient or a template: those come only from real records.
        endpoints.MapGet(ListPath, List).RequireAuthorization("Administration");
        // One administrative test notice through the normal pipeline (see TemplateTest).
        endpoints.MapPost(TemplateTestPath, TemplateTest).RequireAuthorization("Administration")
            .AddEndpointFilter<Auth.AntiforgeryFilter>();
        return endpoints;
    }

    /// <summary>
    /// Sends ONE real notice to validate Meta end to end while the worker stays disabled. It is not a second sending
    /// path: it builds the notice with the normal factory from an existing record of the person whose number was given
    /// (nothing is created or changed in reservations or visits), marks it TEST:{business key} and has the normal
    /// dispatcher claim and process only that notice — composer (template by type, opt-in gate, reschedule link),
    /// WhatsAppCloudApiService, wamid, webhook. No free text, no template name, no recipient other than the record's own
    /// number. The same record and type can be tested once: a repeat is refused, never resent.
    /// </summary>
    private static async Task<IResult> TemplateTest(WhatsappTemplateTestRequest request, HttpContext context,
        ApplicationDbContext db, WhatsAppNotificationDispatcher dispatcher, IOptionsMonitor<WhatsAppTemplateOptions> templates,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var type = WhatsAppNotificationConfiguration.TypeStorage
            .Where(x => x.Value == (request.NotificationType ?? "").Trim().ToUpperInvariant()).Select(x => (WhatsAppNotificationType?)x.Key)
            .SingleOrDefault();
        if (type is not { } notificationType || !TestableTypes.Contains(notificationType))
            return Results.BadRequest(new ApiError("INVALID_TYPE", "O tipo de notificação informado é inválido."));
        if (!GestaoPredio.Domain.Professionals.WhatsAppNormalizer.TryNormalize(request.Phone ?? "", out var phone))
            return Results.BadRequest(new ApiError("WHATSAPP_RECIPIENT_INVALID", "Informe um número de WhatsApp válido."));
        if (templates.CurrentValue.NameFor(notificationType).Length == 0)
            return Results.Json(new ApiError("TEMPLATE_NOT_CONFIGURED", "O template deste tipo não está configurado."),
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var now = time.GetUtcNow();
        var built = await BuildFromExistingRecordAsync(db, notificationType, phone, now, cancellationToken);
        if (built.Error is { } error) return error;
        var notification = built.Notification!.AsAdminTest();

        // Double click / repeated request: the unique key refuses the second notice, so nothing is sent twice.
        if (await db.WhatsAppNotifications.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == notification.IdempotencyKey,
                cancellationToken) is { } existing)
            return AlreadyRequested(existing);
        db.WhatsAppNotifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.WhatsAppNotifications.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == notification.IdempotencyKey, cancellationToken);
            if (winner is null) throw;
            return AlreadyRequested(winner);
        }

        await dispatcher.DispatchOneAsync(notification.Id, cancellationToken);

        db.ChangeTracker.Clear();
        var result = await db.WhatsAppNotifications.AsNoTracking().SingleAsync(x => x.Id == notification.Id, cancellationToken);
        var status = WhatsAppNotificationConfiguration.StatusStorage[result.Status];
        var typeName = WhatsAppNotificationConfiguration.TypeStorage[notificationType];
        // Who, which notice, outcome, when, and the wamid if any. Never the number, a name, a parameter or a link.
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = $"{TemplateTestAuditAction}:{typeName}",
            Result = status,
            ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            TargetEntityType = "WHATSAPP_NOTIFICATION",
            TargetEntityId = result.Id,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = time.GetUtcNow(),
            CorrelationId = result.MessageId is { } wamid ? (wamid.Length > 100 ? wamid[..100] : wamid) : $"no-wamid:{result.Id:N}"
        });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new WhatsappTemplateTestResponse(result.Id, typeName, status, result.LastErrorCode, result.MessageId));
    }

    private static IResult AlreadyRequested(WhatsAppNotification existing) =>
        Results.Json(new
        {
            code = "TEMPLATE_TEST_ALREADY_REQUESTED",
            message = "Este teste já foi solicitado para este registro; ele não é reenviado.",
            notificationId = existing.Id,
            status = WhatsAppNotificationConfiguration.StatusStorage[existing.Status]
        }, statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// The composer needs a real record for each notice; the most recent eligible one of that person is used, read-only.
    /// CLIENT_CHECKED_IN goes to a professional (a visit WAITING for them); the other five go to a customer.
    /// </summary>
    private static async Task<(WhatsAppNotification? Notification, IResult? Error)> BuildFromExistingRecordAsync(
        ApplicationDbContext db, WhatsAppNotificationType type, string phone, DateTimeOffset now, CancellationToken cancellationToken)
    {
        static (WhatsAppNotification?, IResult?) NotFound() =>
            (null, Results.NotFound(new ApiError("RECIPIENT_NOT_FOUND", "Nenhum cadastro ativo com este número para este tipo de aviso.")));
        static (WhatsAppNotification?, IResult?) NoRecord() =>
            (null, Results.Json(new ApiError("NO_ELIGIBLE_RECORD", "Não há atendimento desta pessoa em que este aviso se aplique."),
                statusCode: StatusCodes.Status409Conflict));

        if (type == WhatsAppNotificationType.ClientCheckedIn)
        {
            // WhatsApp is not unique among professionals: any active one with this number may have the waiting visit.
            var professionalIds = await db.Professionals.AsNoTracking()
                .Where(x => x.WhatsApp == phone && x.IsActive).Select(x => x.Id).ToListAsync(cancellationToken);
            if (professionalIds.Count == 0) return NotFound();
            var visit = await db.Visits.AsNoTracking()
                .Where(x => professionalIds.Contains(x.ProfessionalId) && x.Status == VisitStatus.Waiting)
                .OrderByDescending(x => x.ArrivedAt).FirstOrDefaultAsync(cancellationToken);
            return visit is null ? NoRecord() : (WhatsAppNotification.ClientCheckedIn(visit, now), null);
        }

        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedPhone == phone && x.IsActive, cancellationToken);
        if (customer is null) return NotFound();
        var reservations = db.Reservations.AsNoTracking().Where(x => x.CustomerId == customer.Id);
        Reservation? reservation = type switch
        {
            WhatsAppNotificationType.ProfessionalDelayed => await reservations
                .Where(r => r.Status == ReservationStatus.Approved && r.Kind != ReservationKind.Cancellation && r.StartAt <= now &&
                            db.Visits.Any(v => v.ReservationId == r.Id && v.Status == VisitStatus.Waiting))
                .OrderByDescending(r => r.StartAt).FirstOrDefaultAsync(cancellationToken),
            WhatsAppNotificationType.ProfessionalCancelled => await reservations
                .Where(r => r.Status == ReservationStatus.Cancelled && r.CancellationReason == ReservationCancellationReason.ProfessionalUnavailable)
                .OrderByDescending(r => r.UpdatedAt).FirstOrDefaultAsync(cancellationToken),
            WhatsAppNotificationType.AppointmentCancelled => await reservations
                .Where(r => r.Status == ReservationStatus.Cancelled && r.CancellationReason != ReservationCancellationReason.ProfessionalUnavailable)
                .OrderByDescending(r => r.UpdatedAt).FirstOrDefaultAsync(cancellationToken),
            WhatsAppNotificationType.AppointmentRescheduled => await reservations
                .Where(r => r.Status == ReservationStatus.Approved && r.OriginalReservationId != null && r.StartAt > now)
                .OrderBy(r => r.StartAt).FirstOrDefaultAsync(cancellationToken),
            _ => await reservations
                .Where(r => r.Status == ReservationStatus.Approved && r.Kind != ReservationKind.Cancellation && r.StartAt > now)
                .OrderBy(r => r.StartAt).FirstOrDefaultAsync(cancellationToken)
        };
        if (reservation is null) return NoRecord();
        var notification = type switch
        {
            WhatsAppNotificationType.ProfessionalDelayed => WhatsAppNotification.ProfessionalDelayed(reservation, 0, now),
            WhatsAppNotificationType.ProfessionalCancelled or WhatsAppNotificationType.AppointmentCancelled =>
                WhatsAppNotification.ReservationCancelled(reservation, now),
            WhatsAppNotificationType.AppointmentRescheduled => WhatsAppNotification.AppointmentRescheduled(reservation, now),
            _ => WhatsAppNotification.AppointmentConfirmed(reservation, now)
        };
        return (notification, null);
    }

    private static async Task<IResult> List(int? page, int? pageSize, string? status, string? type,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var actualPage = page ?? 1;
        var actualSize = pageSize ?? 20;
        if (actualPage < 1 || actualSize is < 1 or > 100)
            return Results.BadRequest(new ApiError("INVALID_PAGE", "A paginação informada é inválida."));

        var query = db.WhatsAppNotifications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var match = WhatsAppNotificationConfiguration.StatusStorage.Where(x => x.Value == status.Trim().ToUpperInvariant())
                .Select(x => (WhatsAppNotificationStatus?)x.Key).SingleOrDefault();
            if (match is null) return Results.BadRequest(new ApiError("INVALID_STATUS", "O status informado é inválido."));
            query = query.Where(x => x.Status == match);
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            var match = WhatsAppNotificationConfiguration.TypeStorage.Where(x => x.Value == type.Trim().ToUpperInvariant())
                .Select(x => (WhatsAppNotificationType?)x.Key).SingleOrDefault();
            if (match is null) return Results.BadRequest(new ApiError("INVALID_TYPE", "O tipo informado é inválido."));
            query = query.Where(x => x.Type == match);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<WhatsappNotificationResponse>(rows.Select(x => new WhatsappNotificationResponse(
            x.Id,
            WhatsAppNotificationConfiguration.TypeStorage[x.Type],
            WhatsAppNotificationConfiguration.RecipientStorage[x.Recipient],
            WhatsAppNotificationConfiguration.StatusStorage[x.Status],
            x.Attempts, x.NextAttemptAt, x.LastErrorCode, x.MessageId,
            x.ReservationId, x.VisitId, x.ProfessionalId, x.CustomerId, x.CreatedAt, x.UpdatedAt)).ToArray(),
            actualPage, actualSize, total));
    }
}
