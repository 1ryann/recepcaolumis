using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalAvailabilityReservationMutationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private static readonly DateOnly Monday = new(2027, 1, 4);

    [Fact]
    public async Task Reservation_creation_fails_closed_without_operating_hours()
    {
        await factory.ResetAsync();
        var (professional, room) = await SeedResourcesAsync(custom: false, addCustomMonday: false);
        await LoginOperationsAsync();

        var response = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = room.Id,
            professionalId = professional.Id,
            startAt = Utc(new(9, 0)),
            endAt = Utc(new(10, 0))
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("OPERATING_HOURS_NOT_CONFIGURED",
            (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Reservation_creation_uses_custom_schedule_after_resource_locks()
    {
        await factory.ResetAsync();
        var (professional, room) = await SeedResourcesAsync(custom: true, addCustomMonday: true);
        await SeedOperatingHoursAsync();
        await LoginOperationsAsync();

        var outside = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = room.Id, professionalId = professional.Id,
            startAt = Utc(new(11, 0)), endAt = Utc(new(12, 0))
        });
        var inside = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = room.Id, professionalId = professional.Id,
            startAt = Utc(new(9, 0)), endAt = Utc(new(10, 0))
        });

        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        Assert.Equal("PROFESSIONAL_UNAVAILABLE", (await outside.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Equal(HttpStatusCode.Created, inside.StatusCode);
    }

    private async Task<(Professional Professional, Room Room)> SeedResourcesAsync(bool custom, bool addCustomMonday)
    {
        var now = factory.UtcNow;
        var professional = Professional.Create("Profissional Agenda", "Terapia", $"699{Random.Shared.Next(10000000, 99999999)}", now);
        if (custom) professional.SetAvailabilityMode(ProfessionalAvailabilityMode.Custom, now);
        var room = Room.Create($"Sala Agenda {Guid.NewGuid():N}", null, 3, 75m, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(professional, room);
        if (addCustomMonday)
            db.ProfessionalAvailabilityIntervals.AddRange(ProfessionalAvailabilityInterval.CreateDay(
                professional.Id, DayOfWeek.Monday, [new(new(9, 0), new(10, 0))]));
        await db.SaveChangesAsync();
        return (professional, room);
    }

    private async Task SeedOperatingHoursAsync()
    {
        var now = factory.UtcNow;
        var schedule = OperatingHoursSchedule.Create(now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.OperatingHoursSchedules.Add(schedule);
        db.OperatingHourIntervals.AddRange(OperatingHourInterval.CreateDay(schedule.Id, DayOfWeek.Monday,
            [new(new(8, 0), new(18, 0))]));
        await db.SaveChangesAsync();
    }

    private async Task LoginOperationsAsync()
    {
        var email = $"operations-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private static DateTimeOffset Utc(TimeOnly time)
    {
        var local = Monday.ToDateTime(time, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    private sealed record ErrorPayload(string Code, string Message);
}
