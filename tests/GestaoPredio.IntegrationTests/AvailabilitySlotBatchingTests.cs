using System.Diagnostics;
using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

// Listing a day's slots used to run the full per-period check for every 15-minute candidate: about nine queries
// each (operating hours twice, room blocks, then six reservation/lease/occurrence probes). A 08:00–18:00 day is
// 37 candidates for a one-hour appointment, so ~330 sequential round trips. Against a database a few hundred
// milliseconds away (the VPS on a Supabase sa-east-1 project) that is over a minute, and the proxy answered 504:
// no customer could see a single slot. The day is now loaded once and evaluated in memory.
//
// The equivalence test is the one that matters: for every candidate it compares the batched list with
// FindAvailableRoomAsync, the exact per-period check that booking still runs under the resource lock. Each seeded
// conflict is there to catch one specific way of getting the in-memory rules wrong.
[Collection(ModulesDatabaseCollection.Name)]
public sealed class AvailabilitySlotBatchingTests(ModulesApiFactory factory)
{
    private static readonly DateOnly Monday = new(2027, 1, 4);

    [Fact]
    public async Task The_day_s_slot_list_agrees_with_the_exact_check_for_every_candidate()
    {
        var seed = await SeedDayAsync();

        IReadOnlyList<AppointmentAvailabilitySlot> slots;
        await using (var scope = factory.Services.CreateAsyncScope())
            slots = await scope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>()
                .FindSlotsAsync(seed.ProfessionalId, Monday, 60, default);

        var expected = new List<DateTimeOffset>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            for (var start = new TimeOnly(8, 0); start <= new TimeOnly(17, 0); start = start.AddMinutes(15))
            {
                var startAt = Utc(Monday, start);
                if ((await service.FindAvailableRoomAsync(seed.ProfessionalId, startAt, startAt.AddHours(1),
                        null, null, default)).IsAvailable)
                    expected.Add(startAt);
            }
        }

        Assert.Equal(expected, slots.Select(slot => slot.StartAt).ToList());
        // Guard the oracle itself: the scenario must leave some slots open and close others, or the comparison
        // above would pass for an engine that always answers "none" or "all".
        Assert.Contains(Utc(Monday, new TimeOnly(9, 0)), expected);   // room 1 blocked at 08:00-09:00 only
        Assert.DoesNotContain(Utc(Monday, new TimeOnly(10, 0)), expected); // both rooms taken, room 3 ending-pending
        Assert.DoesNotContain(Utc(Monday, new TimeOnly(11, 0)), expected); // the professional is booked elsewhere
        Assert.DoesNotContain(Utc(Monday, new TimeOnly(15, 0)), expected); // lease occurrence + reservation
        Assert.Contains(Utc(Monday, new TimeOnly(16, 0)), expected);  // the cancelled reservation frees room 2
    }

    [Fact]
    public async Task Listing_a_day_s_slots_costs_a_fixed_handful_of_queries_not_a_batch_per_slot()
    {
        var seed = await SeedDayAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>();
        using var counter = new CommandCounter(db);

        var slots = await service.FindSlotsAsync(seed.ProfessionalId, Monday, 60, default);

        Assert.NotEmpty(slots);
        // Nine queries per candidate before, ~330 for this day. A fixed load no longer grows with the day's length.
        Assert.True(counter.Count <= 15, $"listing the day ran {counter.Count} queries");
    }

    private async Task<Seed> SeedDayAsync()
    {
        await factory.ResetAsync();
        var now = factory.UtcNow;
        var professional = Professional.Create("Agenda em lote", "Fisioterapia", "69999990011", now);
        var other = Professional.Create("Outro profissional", "Psicologia", "69999990012", now);
        var room1 = Room.Create($"Sala lote 1 {Guid.NewGuid():N}", null, 4, 100m, now);
        var room2 = Room.Create($"Sala lote 2 {Guid.NewGuid():N}", null, 4, 100m, now);
        var room3 = Room.Create($"Sala lote 3 {Guid.NewGuid():N}", null, 4, 100m, now);
        var schedule = OperatingHoursSchedule.Create(now);
        var hours = OperatingHourInterval.CreateDay(schedule.Id, DayOfWeek.Monday, [new(new(8, 0), new(18, 0))]);
        var tenant = Tenant.Create("Locatário lote", TenantKind.Individual, now);

        Reservation Booked(Room room, Professional who, int hour) => Reservation.CreateApproved(room.Id, who.Id,
            Utc(Monday, new TimeOnly(hour, 0)), Utc(Monday, new TimeOnly(hour + 1, 0)), "seed", now);

        // 08:00 room 1 blocked, room 2 free -> open.
        var block = RoomBlock.Create(room1.Id, Utc(Monday, new(8, 0)), Utc(Monday, new(9, 0)), "Manutenção", "seed", now);
        // 09:00 room 1 taken by someone else, room 2 free -> open.
        var at9 = Booked(room1, other, 9);
        // 10:00 both rooms taken; room 3 is only "free" if an ending-pending lease is ignored -> closed.
        var at10a = Booked(room1, other, 10);
        var at10b = Booked(room2, other, 10);
        var ending = Lease.Create(tenant.Id, other.Id, room3.Id, LeaseMode.Hourly, 50m, now.AddDays(-2), now.Day,
            now.AddDays(-2), now.AddDays(-2).AddHours(1), null, now);
        ending.MarkEndingPending(now);
        // 11:00 the professional is booked in room 2, room 1 is free -> closed anyway.
        var own = Booked(room2, professional, 11);
        // 15:00 room 1 held by a lease occurrence (the lease itself starts later), room 2 taken -> closed.
        var future = Lease.Create(tenant.Id, other.Id, room1.Id, LeaseMode.Hourly, 50m, now, now.Day,
            Utc(Monday.AddDays(30), new(8, 0)), Utc(Monday.AddDays(30), new(9, 0)), null, now);
        var occurrence = LeaseOccurrence.Create(future.Id, Utc(Monday, new(15, 0)), Utc(Monday, new(16, 0)), now);
        var at15 = Booked(room2, other, 15);
        // 16:00 room 1 taken, room 2's reservation was cancelled -> open.
        var at16 = Booked(room1, other, 16);
        var cancelled = Booked(room2, other, 16);
        cancelled.Cancel("seed", now);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(professional, other, room1, room2, room3, schedule, tenant);
        db.AddRange(hours);
        db.AddRange(block, at9, at10a, at10b, ending, own, future, occurrence, at15, at16, cancelled);
        await db.SaveChangesAsync();
        return new Seed(professional.Id);
    }

    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")));

    private sealed record Seed(Guid ProfessionalId);

    // Counts the commands one DbContext instance executes, through EF Core's DiagnosticListener, so commands from
    // tests running in parallel on other contexts are never counted.
    private sealed class CommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly DbContext context;
        private readonly List<IDisposable> subscriptions = [];
        private int count;

        public CommandCounter(DbContext context)
        {
            this.context = context;
            subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        }

        public int Count => Volatile.Read(ref count);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name) subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuted.Name &&
                value.Value is CommandExecutedEventData data && ReferenceEquals(data.Context, context))
                Interlocked.Increment(ref count);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { foreach (var subscription in subscriptions) subscription.Dispose(); }
    }
}
