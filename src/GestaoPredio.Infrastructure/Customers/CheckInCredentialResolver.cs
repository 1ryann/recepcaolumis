using System.Security.Cryptography;
using GestaoPredio.Application.Customers;
using GestaoPredio.Application.Visits;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Customers;

/// <summary>
/// Moved out of the kiosk endpoint so every reader shares one definition of "this credential is good".
/// The plaintext is hashed here and compared against the stored hash; nothing about the credential is
/// returned, logged or thrown.
/// </summary>
public sealed class CheckInCredentialResolver(
    ApplicationDbContext db,
    IManualCheckInCodeHasher hasher,
    TimeProvider time) : ICheckInCredentialResolver
{
    public async Task<ResolvedCheckInCredential?> ResolveAsync(
        string presented,
        bool allowUsed,
        CancellationToken cancellationToken)
    {
        // Dispatch by string shape (spec 7A.6): a 6-digit manual code is looked up by its keyed
        // HMAC; anything else keeps the strong-token path (Base64Url -> 32 bytes -> SHA-256).
        var value = (presented ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        byte[] hash;
        if (ManualCheckInCode.TryParse(value, out var code))
        {
            hash = hasher.Hash(code);
        }
        else
        {
            byte[] bytes;
            try { bytes = WebEncoders.Base64UrlDecode(value); } catch (FormatException) { return null; }
            if (bytes.Length != 32) return null;
            hash = SHA256.HashData(bytes);
        }

        var row = await (from token in db.CheckInTokens.AsNoTracking()
                         join reservation in db.Reservations.AsNoTracking() on token.ReservationId equals reservation.Id
                         join customer in db.Customers.AsNoTracking() on reservation.CustomerId equals customer.Id
                         join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
                         join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
                         where token.TokenHash == hash || token.ManualCodeHash == hash
                         select new
                         {
                             token,
                             reservation,
                             customer,
                             ProfessionalName = professional.Name,
                             RoomName = room.Name
                         }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var now = time.GetUtcNow();
        if (row.token.RevokedAt is not null || row.token.ExpiresAt <= now
            || (!allowUsed && row.token.UsedAt is not null)
            || row.reservation.Status != ReservationStatus.Approved || !row.customer.IsActive
            || !CheckInWindow.IsOpen(row.reservation.StartAt, row.reservation.EndAt, now)) return null;

        return new ResolvedCheckInCredential(
            row.reservation.Id,
            row.customer.Id,
            row.customer.Name,
            row.ProfessionalName,
            row.RoomName,
            row.reservation.StartAt,
            row.reservation.EndAt,
            row.token.UsedAt is not null);
    }
}
