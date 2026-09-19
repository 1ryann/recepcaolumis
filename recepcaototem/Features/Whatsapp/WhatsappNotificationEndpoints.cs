using GestaoPredio.Domain.Notifications;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
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

public static class WhatsappNotificationEndpoints
{
    public const string ListPath = "/api/admin/whatsapp/notifications";

    public static IEndpointRouteBuilder MapWhatsappNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Read-only. Nothing here can send, resend, pick a recipient or a template: those come only from real records.
        endpoints.MapGet(ListPath, List).RequireAuthorization("Administration");
        return endpoints;
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
