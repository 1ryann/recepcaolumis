using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalAvailabilityExceptionApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private static readonly DateOnly Monday = new(2027, 1, 4);

    [Fact]
    public async Task Operations_manage_exceptions_with_overlap_concurrency_warning_and_audit()
    {
        await factory.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create("Exceção API", "Clínica", "69999993333", now);
        var room = Room.Create("Sala Exceção", null, 3, 80m, now);
        var schedule = OperatingHoursSchedule.Create(now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id,
            Utc(new(9, 0)), Utc(new(10, 0)), "seed", now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(professional, room, schedule, reservation);
            db.OperatingHourIntervals.AddRange(OperatingHourInterval.CreateDay(schedule.Id,
                DayOfWeek.Monday, [new(new(8, 0), new(18, 0))]));
            await db.SaveChangesAsync();
        }
        var email = $"manager-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
        var basePath = $"/api/admin/professionals/{professional.Id}/availability/exceptions";

        var createdResponse = await factory.PostWithCsrfAsync(basePath, new
        {
            date = Monday, allDay = false, startTime = "09:00", endTime = "10:00", reason = " Compromisso "
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<ExceptionPayload>())!;
        Assert.Equal("Compromisso", created.Reason);
        Assert.Equal(1, created.ExistingReservationsOutsideAvailabilityCount);

        var overlap = await factory.PostWithCsrfAsync(basePath, new
        {
            date = Monday, allDay = true, startTime = (string?)null, endTime = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        var updatedResponse = await factory.PutWithCsrfAsync($"{basePath}/{created.Id}", new
        {
            date = Monday, allDay = false, startTime = "10:00", endTime = "11:00",
            reason = (string?)null, concurrencyToken = created.ConcurrencyToken
        });
        updatedResponse.EnsureSuccessStatusCode();
        var stale = await factory.PutWithCsrfAsync($"{basePath}/{created.Id}", new
        {
            date = Monday, allDay = false, startTime = "11:00", endTime = "12:00",
            reason = (string?)null, concurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var listed = await factory.Client.GetFromJsonAsync<ExceptionPayload[]>(
            $"{basePath}?from=2027-01-01&to=2027-12-31");
        Assert.Single(listed!);
        var current = (await updatedResponse.Content.ReadFromJsonAsync<ExceptionPayload>())!;
        var removed = await factory.DeleteWithCsrfAsync($"{basePath}/{created.Id}",
            new { concurrencyToken = current.ConcurrencyToken });
        removed.EnsureSuccessStatusCode();

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await verify.ProfessionalAvailabilityExceptions.AnyAsync());
        Assert.Equal(3, await verify.AuditEntries.CountAsync(entry =>
            entry.Action.Contains("PROFESSIONAL_AVAILABILITY_EXCEPTION")));
        Assert.Equal(ReservationStatus.Approved,
            (await verify.Reservations.SingleAsync(value => value.Id == reservation.Id)).Status);
    }

    private static DateTimeOffset Utc(TimeOnly time)
    {
        var local = Monday.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,
            TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")));
    }

    private sealed record ExceptionPayload(Guid Id, Guid ProfessionalId, DateOnly Date, bool AllDay,
        string? StartTime, string? EndTime, string? Reason, DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt, string ConcurrencyToken, int ExistingReservationsOutsideAvailabilityCount);
}
