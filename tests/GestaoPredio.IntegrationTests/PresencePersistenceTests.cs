using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class PresencePersistenceTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Presence_tokens_and_reschedule_tokens_round_trip_with_row_versions()
    {
        await factory.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create("Presença", "Clínica", "69999990000", now);
        var room = Room.Create("Sala Presença", null, 1, 50m, now);
        var customer = Customer.Create("Cliente Presença", "69988887777", now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, now.AddHours(2), now.AddHours(3), "seed", now, customer.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(professional, room, customer, reservation);
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(professional.Id, now));
            db.ProfessionalPresenceTokens.Add(ProfessionalPresenceToken.Create(professional.Id, RandomNumberGenerator.GetBytes(32), now, now.AddSeconds(120)));
            reservation.Cancel("seed", now, ReservationCancellationReason.ProfessionalUnavailable);
            db.RescheduleTokens.Add(RescheduleToken.Create(reservation.Id, RandomNumberGenerator.GetBytes(32), now, now.AddHours(48)));
            await db.SaveChangesAsync();
        }

        await using (var verify = factory.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var presence = await db.ProfessionalPresences.AsNoTracking().SingleAsync(x => x.ProfessionalId == professional.Id);
            Assert.True(presence.IsOpen);
            Assert.NotEqual(0u, presence.Version);
            Assert.Equal(PresenceSource.QrSelfScan, presence.Source);

            var presenceToken = await db.ProfessionalPresenceTokens.AsNoTracking().SingleAsync(x => x.ProfessionalId == professional.Id);
            Assert.Equal(32, presenceToken.TokenHash.Length);

            var rescheduleToken = await db.RescheduleTokens.AsNoTracking().SingleAsync(x => x.ReservationId == reservation.Id);
            Assert.Equal(32, rescheduleToken.TokenHash.Length);

            var storedReservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservation.Id);
            Assert.Equal(ReservationCancellationReason.ProfessionalUnavailable, storedReservation.CancellationReason);
        }
    }

    [Fact]
    public async Task A_second_open_presence_for_the_same_professional_is_rejected_by_the_partial_unique_index()
    {
        await factory.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create("Presença Única", "Clínica", "69999990001", now);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(professional.Id, now));
        await db.SaveChangesAsync();

        db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(professional.Id, now.AddMinutes(1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Existing_reservations_and_exceptions_default_the_new_columns()
    {
        await factory.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create("Default Colunas", "Clínica", "69999990002", now);
        var room = Room.Create("Sala Default", null, 1, 50m, now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, now.AddHours(2), now.AddHours(3), "seed", now);
        var exception = ProfessionalAvailabilityException.Create(professional.Id, DateOnly.FromDateTime(now.UtcDateTime).AddDays(1), true, null, null, null, now);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(professional, room, reservation, exception);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(ReservationCancellationReason.None,
            (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservation.Id)).CancellationReason);
        Assert.Equal(ProfessionalAvailabilityExceptionOrigin.Planned,
            (await db.ProfessionalAvailabilityExceptions.AsNoTracking().SingleAsync(x => x.Id == exception.Id)).Origin);
    }
}
