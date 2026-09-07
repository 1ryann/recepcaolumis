using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace recepcaototem.Features.Reservations;

internal static class ReservationCheckInTokenRevocation
{
    internal static async Task RevokeAsync(
        ApplicationDbContext db,
        Guid reservationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var token = await db.CheckInTokens.SingleOrDefaultAsync(
            value => value.ReservationId == reservationId, cancellationToken);
        token?.Revoke(occurredAt);
    }
}
