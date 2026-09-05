using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Persistence;

namespace recepcaototem.Features.Auth;

public sealed class AuthAuditService(ApplicationDbContext db, TimeProvider timeProvider)
{
    public async Task WriteAsync(
        string action,
        string result,
        string? actorUserId,
        string? targetUserId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = action,
            Result = result,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = timeProvider.GetUtcNow(),
            CorrelationId = context.TraceIdentifier
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
