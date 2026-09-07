using System.Security.Claims;
using GestaoPredio.Application.AccessControl;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.AccessControl;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.AccessControl;

public static class AccessControlEndpoints
{
    private const string DoorTargetType = "ACCESS_DOOR";
    private const string RequestedAction = "DOOR_RELEASE_REQUESTED";
    private const string SucceededAction = "DOOR_RELEASE_SUCCEEDED";
    private const string FailedAction = "DOOR_RELEASE_FAILED";

    public static IEndpointRouteBuilder MapAccessControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/reception/access/door/release", Release)
            .RequireAuthorization("Operations")
            .AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> Release(
        ReleaseDoorRequest request,
        HttpContext context,
        ApplicationDbContext db,
        IAccessControlService accessControl,
        IAccessControlProvider provider,
        AccessControlCooldown cooldown,
        IConfiguration configuration,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (request.VisitId is Guid visitId && !await db.Visits.AsNoTracking().AnyAsync(x => x.Id == visitId, cancellationToken))
            return Results.NotFound();

        var doorId = configuration["AccessControl:DefaultDoor"];
        if (string.IsNullOrWhiteSpace(doorId) || doorId.Length > 100)
            return Results.Json(new ApiError("ACCESS_CONTROL_NOT_CONFIGURED", "O controle de acesso não está configurado."), statusCode: StatusCodes.Status503ServiceUnavailable);

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = time.GetUtcNow();
        await WriteAuditAsync(db, context, actor, doorId, "", RequestedAction, "SUCCEEDED", now, cancellationToken);

        if (!cooldown.TryAcquire(doorId, now))
        {
            await WriteAuditAsync(db, context, actor, doorId, provider.Name, FailedAction, "FAILED", now, cancellationToken, "ACCESS_CONTROL_COOLDOWN");
            return Results.Json(new ApiError("ACCESS_CONTROL_COOLDOWN", "A porta foi acionada recentemente. Tente novamente em instantes."), statusCode: StatusCodes.Status429TooManyRequests);
        }

        var result = await accessControl.ReleaseDoorAsync(new AccessControlReleaseRequest(doorId, request.VisitId, actor ?? ""), cancellationToken);
        var action = result.Success ? SucceededAction : FailedAction;
        await WriteAuditAsync(db, context, actor, doorId, result.Provider, action,
            result.Success ? "SUCCEEDED" : "FAILED", time.GetUtcNow(), cancellationToken, result.FailureCode);

        var response = new ReleaseDoorResponse(result.Success, result.CommandAccepted, result.Provider, result.FailureCode);
        return result.Success
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task WriteAuditAsync(ApplicationDbContext db, HttpContext context, string? actor,
        string doorId, string provider, string action, string result, DateTimeOffset occurredAt,
        CancellationToken cancellationToken, string? failureCode = null)
    {
        var suffix = string.IsNullOrWhiteSpace(failureCode) ? "" : $":{failureCode}";
        var correlation = $"{context.TraceIdentifier}:{doorId}:{provider}{suffix}";
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actor,
            Action = action,
            Result = result,
            OccurredAt = occurredAt,
            CorrelationId = correlation.Length > 100 ? correlation[..100] : correlation,
            TargetEntityType = DoorTargetType
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
