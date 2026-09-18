using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalAvailabilitySchedulingTests(ModulesApiFactory factory)
{
    private static readonly DateOnly Monday = new(2027, 1, 4);

    [Fact]
    public async Task Central_service_applies_mode_global_intersection_and_exceptions()
    {
        await factory.ResetAsync();
        var now = factory.UtcNow;
        var professional = Professional.Create("Agenda Central", "Psicologia", "69999990001", now);
        var room = Room.Create("Sala Agenda Central", null, 4, 100m, now);
        var schedule = OperatingHoursSchedule.Create(now);
        var global = OperatingHourInterval.CreateDay(schedule.Id, DayOfWeek.Monday,
            [new(new(8, 0), new(12, 0))]);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(professional, room, schedule);
            db.AddRange(global);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var slots = await scope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>()
                .FindSlotsAsync(professional.Id, Monday, 60, default);
            Assert.Contains(slots, slot => Local(slot.StartAt).Hour == 8);
            Assert.Contains(slots, slot => Local(slot.StartAt).Hour == 11);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tracked = await db.Professionals.SingleAsync(value => value.Id == professional.Id);
            tracked.SetAvailabilityMode(ProfessionalAvailabilityMode.Custom, now.AddMinutes(1));
            db.ProfessionalAvailabilityIntervals.AddRange(
                ProfessionalAvailabilityInterval.CreateDay(professional.Id, DayOfWeek.Monday,
                    [new(new(9, 0), new(11, 0))]));
            db.ProfessionalAvailabilityExceptions.Add(ProfessionalAvailabilityException.Create(
                professional.Id, Monday, false, new(9, 30), new(10, 0), null, now));
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var slots = await scope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>()
                .FindSlotsAsync(professional.Id, Monday, 30, default);
            var localStarts = slots.Select(slot => Local(slot.StartAt).ToString("HH:mm")).ToArray();
            Assert.Equal(["09:00", "10:00", "10:15", "10:30"], localStarts);
        }
    }

    [Fact]
    public async Task Missing_operating_hours_fails_closed_for_query_and_confirmation()
    {
        await factory.ResetAsync();
        var now = factory.UtcNow;
        var professional = Professional.Create("Sem Expediente", "Clínica", "69999990002", now);
        var room = Room.Create("Sala Sem Expediente", null, 4, 100m, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(professional, room);
            await db.SaveChangesAsync();
        }

        await using var checkScope = factory.Services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = checkScope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>();
        Assert.Empty(await service.FindSlotsAsync(professional.Id, Monday, 60, default));
        await using var transaction = await checkDb.Database.BeginTransactionAsync();
        var startAt = Utc(Monday, new(8, 0));
        var result = await service.FindAvailableRoomAsync(
            professional.Id, startAt, startAt.AddHours(1), room.Id, null, default);
        Assert.Equal(AppointmentAvailabilityFailure.OperatingHoursNotConfigured, result.Failure);
    }

    private static DateTimeOffset Utc(DateOnly date, TimeOnly time)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    private static DateTime Local(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")).DateTime;
}
