using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class OperatingHoursRoomBlocksApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private static readonly DateTimeOffset MondayAtEight = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Operations_manage_multiple_intervals_and_closed_days_with_audit_and_concurrency()
    {
        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Gerente);

        var createdResponse = await factory.PutWithCsrfAsync("/api/admin/operating-hours", ScheduleBody());
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<SchedulePayload>())!;
        Assert.True(created.Configured);
        Assert.NotEmpty(created.ConcurrencyToken!);
        Assert.Equal(2, created.Days.Single(day => day.DayOfWeek == "MONDAY").Intervals.Count);
        Assert.Empty(created.Days.Single(day => day.DayOfWeek == "SUNDAY").Intervals);

        var current = (await (await factory.Client.GetAsync("/api/admin/operating-hours"))
            .Content.ReadFromJsonAsync<SchedulePayload>())!;
        Assert.Equal(created.ConcurrencyToken, current.ConcurrencyToken);
        // A later edit gets a later UpdatedAt; with an unmoved frozen clock the schedule row would not change.
        factory.AdvanceTime(TimeSpan.FromMinutes(1));
        var changed = await factory.PutWithCsrfAsync("/api/admin/operating-hours",
            ScheduleBody(created.ConcurrencyToken, mondayClose: "17:00"));
        changed.EnsureSuccessStatusCode();
        Assert.NotEqual(created.ConcurrencyToken,
            (await changed.Content.ReadFromJsonAsync<SchedulePayload>())!.ConcurrencyToken);

        var stale = await factory.PutWithCsrfAsync("/api/admin/operating-hours",
            ScheduleBody(created.ConcurrencyToken, mondayClose: "16:00"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.OperatingHoursUpdated &&
            entry.TargetEntityType == AuditTargetTypes.OperatingHours));
    }

    [Fact]
    public async Task Room_blocks_are_listed_updated_and_cancelled_with_xmin_and_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var response = await factory.PostWithCsrfAsync("/api/admin/room-blocks", new
        {
            roomId = resources.Room.Id,
            startAt = MondayAtEight,
            endAt = MondayAtEight.AddHours(1),
            reason = "Manutenção preventiva"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<RoomBlockPayload>())!;
        Assert.Equal("ACTIVE", created.Status);
        Assert.NotEmpty(created.ConcurrencyToken);

        var page = (await (await factory.Client.GetAsync(
            $"/api/admin/room-blocks?roomId={resources.Room.Id}&status=ACTIVE&page=1&pageSize=20"))
            .Content.ReadFromJsonAsync<RoomBlockPage>())!;
        Assert.Single(page.Items);
        Assert.Equal(created.Id, page.Items[0].Id);

        var updatedResponse = await factory.PutWithCsrfAsync($"/api/admin/room-blocks/{created.Id}", new
        {
            startAt = MondayAtEight.AddHours(1),
            endAt = MondayAtEight.AddHours(2),
            reason = "Limpeza técnica",
            concurrencyToken = created.ConcurrencyToken
        });
        updatedResponse.EnsureSuccessStatusCode();
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<RoomBlockPayload>())!;
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);

        var stale = await factory.PutWithCsrfAsync($"/api/admin/room-blocks/{created.Id}", new
        {
            startAt = MondayAtEight.AddHours(2), endAt = MondayAtEight.AddHours(3), reason = "Reforma",
            concurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var cancelledResponse = await factory.PostWithCsrfAsync($"/api/admin/room-blocks/{created.Id}/cancel",
            new { concurrencyToken = updated.ConcurrencyToken });
        cancelledResponse.EnsureSuccessStatusCode();
        Assert.Equal("CANCELLED", (await cancelledResponse.Content.ReadFromJsonAsync<RoomBlockPayload>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.Action == AuditActions.RoomBlockCreated));
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.Action == AuditActions.RoomBlockUpdated));
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.Action == AuditActions.RoomBlockCancelled));
    }

    [Fact]
    public async Task Room_block_rejects_inactive_room_and_existing_reservation_or_lease()
    {
        await factory.ResetAsync();
        var first = await SeedResourcesAsync();
        var second = await SeedResourcesAsync();
        var reservation = Reservation.CreateApproved(first.Room.Id, first.Professional.Id,
            MondayAtEight, MondayAtEight.AddHours(1), "seed", factory.UtcNow);
        var lease = Lease.Create(second.Tenant.Id, second.Professional.Id, second.Room.Id,
            LeaseMode.Hourly, 100, factory.UtcNow, null,
            MondayAtEight, MondayAtEight.AddHours(1), null, factory.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(reservation, lease,
                LeaseOccurrence.Create(lease.Id, MondayAtEight, MondayAtEight.AddHours(1), factory.UtcNow));
            await db.SaveChangesAsync();
        }
        await LoginAsync(SystemRoles.Gerente);

        foreach (var roomId in new[] { first.Room.Id, second.Room.Id })
        {
            var conflict = await factory.PostWithCsrfAsync("/api/admin/room-blocks", new
            {
                roomId, startAt = MondayAtEight.AddMinutes(30), endAt = MondayAtEight.AddHours(2), reason = "Uso interno"
            });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("ROOM_BLOCK_OCCUPANCY_CONFLICT",
                (await conflict.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var room = await db.Rooms.SingleAsync(value => value.Id == first.Room.Id);
            room.Deactivate(factory.UtcNow);
            db.Reservations.Remove(reservation);
            await db.SaveChangesAsync();
        }
        var inactive = await factory.PostWithCsrfAsync("/api/admin/room-blocks", new
        {
            roomId = first.Room.Id, startAt = MondayAtEight, endAt = MondayAtEight.AddHours(1), reason = "Limpeza"
        });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal("INVALID_ROOM_BLOCK_RESOURCE", (await inactive.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Reservations_respect_operating_hours_and_active_room_blocks()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        (await factory.PutWithCsrfAsync("/api/admin/operating-hours", ScheduleBody())).EnsureSuccessStatusCode();

        var inside = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = resources.Room.Id, professionalId = resources.Professional.Id,
            startAt = MondayAtEight, endAt = MondayAtEight.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Created, inside.StatusCode);
        var outside = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = resources.Room.Id, professionalId = resources.Professional.Id,
            startAt = MondayAtEight.AddHours(-1), endAt = MondayAtEight
        });
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        Assert.Equal("ROOM_OUTSIDE_OPERATING_HOURS",
            (await outside.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var other = await SeedResourcesAsync();
        (await factory.PostWithCsrfAsync("/api/admin/room-blocks", new
        {
            roomId = other.Room.Id, startAt = MondayAtEight, endAt = MondayAtEight.AddHours(1), reason = "Manutenção"
        })).EnsureSuccessStatusCode();
        var blocked = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = other.Room.Id, professionalId = other.Professional.Id,
            startAt = MondayAtEight, endAt = MondayAtEight.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("ROOM_BLOCKED", (await blocked.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Operating_hours_cannot_be_reduced_across_an_existing_reservation()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var schedule = (await (await factory.PutWithCsrfAsync("/api/admin/operating-hours", ScheduleBody()))
            .Content.ReadFromJsonAsync<SchedulePayload>())!;
        (await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            roomId = resources.Room.Id, professionalId = resources.Professional.Id,
            startAt = MondayAtEight.AddHours(9), endAt = MondayAtEight.AddHours(10)
        })).EnsureSuccessStatusCode();

        var conflict = await factory.PutWithCsrfAsync("/api/admin/operating-hours",
            ScheduleBody(schedule.ConcurrencyToken, mondayClose: "17:00"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var rejected = (await conflict.Content.ReadFromJsonAsync<ErrorPayload>())!;
        Assert.Equal("OPERATING_HOURS_CONFLICT", rejected.Code);
        // The operator has to be able to find the booking that is in the way.
        Assert.Contains(resources.Room.Name, rejected.Message);
        var unchanged = (await (await factory.Client.GetAsync("/api/admin/operating-hours"))
            .Content.ReadFromJsonAsync<SchedulePayload>())!;
        Assert.Equal(schedule.ConcurrencyToken, unchanged.ConcurrencyToken);
    }


    // Production, 2026-09-25: the database was rebuilt, so no schedule existed. The guard that rejects an
    // out-of-hours lease only runs when a schedule exists, so an hourly lease covering a whole month got in —
    // and from then on no operating hours could be saved at all, because an occupancy that crosses midnight
    // fits inside no daily interval. The setting was locked by the very data it would have prevented.
    [Fact]
    public async Task A_long_hourly_occupancy_created_before_any_schedule_does_not_lock_the_screen()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);

        var lease = await factory.PostWithCsrfAsync("/api/admin/leases",
            LeaseBody(resources, "HOURLY", MondayAtEight, MondayAtEight.AddDays(30)));
        Assert.Equal(HttpStatusCode.Created, lease.StatusCode);

        var saved = await factory.PutWithCsrfAsync("/api/admin/operating-hours", EveryDayBody());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        // Closing a day the occupancy covers is still refused — and the answer now names what is in the way.
        var token = (await saved.Content.ReadFromJsonAsync<SchedulePayload>())!.ConcurrencyToken;
        var closingSunday = await factory.PutWithCsrfAsync("/api/admin/operating-hours",
            EveryDayBody(token, closed: "SUNDAY"));
        Assert.Equal(HttpStatusCode.Conflict, closingSunday.StatusCode);
        var error = (await closingSunday.Content.ReadFromJsonAsync<ErrorPayload>())!;
        Assert.Equal("OPERATING_HOURS_CONFLICT", error.Code);
        Assert.Contains(resources.Room.Name, error.Message);
        Assert.Contains(resources.Tenant.Name, error.Message);
    }
    [Fact]
    public async Task Hourly_lease_respects_hours_and_blocks_while_monthly_is_not_restricted_by_civil_hours()
    {
        await factory.ResetAsync();
        var outsideResources = await SeedResourcesAsync();
        var blockedResources = await SeedResourcesAsync();
        var monthlyResources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        (await factory.PutWithCsrfAsync("/api/admin/operating-hours", ScheduleBody())).EnsureSuccessStatusCode();

        var outside = await factory.PostWithCsrfAsync("/api/admin/leases",
            LeaseBody(outsideResources, "HOURLY", MondayAtEight.AddHours(-1), MondayAtEight));
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        Assert.Equal("ROOM_OUTSIDE_OPERATING_HOURS", (await outside.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        (await factory.PostWithCsrfAsync("/api/admin/room-blocks", new
        {
            roomId = blockedResources.Room.Id, startAt = MondayAtEight,
            endAt = MondayAtEight.AddHours(1), reason = "Uso interno"
        })).EnsureSuccessStatusCode();
        var blocked = await factory.PostWithCsrfAsync("/api/admin/leases",
            LeaseBody(blockedResources, "HOURLY", MondayAtEight, MondayAtEight.AddHours(1)));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("ROOM_BLOCKED", (await blocked.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var monthly = await factory.PostWithCsrfAsync("/api/admin/leases", new
        {
            tenantId = monthlyResources.Tenant.Id,
            professionalId = monthlyResources.Professional.Id,
            roomId = monthlyResources.Room.Id,
            mode = "MONTHLY",
            contractedRate = 500m,
            billingStartAt = MondayAtEight.AddDays(-1),
            billingDueDay = 10,
            occupancyStartAt = MondayAtEight.AddHours(-4),
            occupancyEndAt = (DateTimeOffset?)null
        });
        Assert.Equal(HttpStatusCode.Created, monthly.StatusCode);
    }

    [Fact]
    public async Task Lease_cannot_overlap_an_existing_approved_reservation_for_the_room()
    {
        await factory.ResetAsync();
        var reserved = await SeedResourcesAsync();
        var leaseResources = await SeedResourcesAsync();
        var reservation = Reservation.CreateApproved(reserved.Room.Id, reserved.Professional.Id,
            MondayAtEight, MondayAtEight.AddHours(1), "seed", factory.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
        }
        await LoginAsync(SystemRoles.Administrador);

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", new
        {
            tenantId = leaseResources.Tenant.Id,
            professionalId = leaseResources.Professional.Id,
            roomId = reserved.Room.Id,
            mode = "HOURLY",
            contractedRate = 100m,
            billingStartAt = MondayAtEight.AddDays(-1),
            billingDueDay = 10,
            occupancyStartAt = MondayAtEight.AddMinutes(30),
            occupancyEndAt = (DateTimeOffset?)MondayAtEight.AddHours(2)
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LEASE_RESOURCE_CONFLICT", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Daily_lease_uses_the_civil_day_and_requires_that_day_to_be_open()
    {
        await factory.ResetAsync();
        var openDay = await SeedResourcesAsync();
        var closedDay = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        (await factory.PutWithCsrfAsync("/api/admin/operating-hours", ScheduleBody())).EnsureSuccessStatusCode();

        var monday = await factory.PostWithCsrfAsync("/api/admin/leases",
            LeaseBody(openDay, "DAILY", MondayAtEight, MondayAtEight.AddHours(1)));
        Assert.Equal(HttpStatusCode.Created, monday.StatusCode);

        var sundayStart = MondayAtEight.AddDays(-1);
        var sunday = await factory.PostWithCsrfAsync("/api/admin/leases",
            LeaseBody(closedDay, "DAILY", sundayStart, sundayStart.AddHours(1)));
        Assert.Equal(HttpStatusCode.Conflict, sunday.StatusCode);
        Assert.Equal("ROOM_OUTSIDE_OPERATING_HOURS", (await sunday.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Professional_cannot_access_configuration_and_mutations_require_antiforgery()
    {
        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Profissional);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/admin/operating-hours")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/admin/room-blocks")).StatusCode);

        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PutAsJsonAsync("/api/admin/operating-hours", ScheduleBody())).StatusCode);
    }

    private async Task<(Tenant Tenant, Professional Professional, Room Room)> SeedResourcesAsync()
    {
        var now = factory.UtcNow;
        var tenant = Tenant.Create($"Locatário {Guid.NewGuid():N}", TenantKind.Individual, now);
        var professional = Professional.Create($"Profissional {Guid.NewGuid():N}", "Teste", "65999990002", now);
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 100, 500, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(tenant, professional, room);
        await db.SaveChangesAsync();
        return (tenant, professional, room);
    }

    private async Task LoginAsync(string role)
    {
        var user = await factory.CreateUserAsync($"availability-{Guid.NewGuid():N}@lumis.test", Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);
    }

    private static object LeaseBody((Tenant Tenant, Professional Professional, Room Room) resources,
        string mode, DateTimeOffset startAt, DateTimeOffset endAt) => new
    {
        tenantId = resources.Tenant.Id, professionalId = resources.Professional.Id,
        roomId = resources.Room.Id, mode, contractedRate = 100m,
        billingStartAt = startAt.AddDays(-1), billingDueDay = 10,
        occupancyStartAt = startAt, occupancyEndAt = (DateTimeOffset?)endAt
    };

    private static object ScheduleBody(string? concurrencyToken = null, string mondayClose = "18:00") => new
    {
        days = Enum.GetNames<DayOfWeek>().Select(day => new
        {
            dayOfWeek = day.ToUpperInvariant(),
            intervals = day == nameof(DayOfWeek.Monday)
                ? new[] { new { opensAt = "08:00", closesAt = "12:00" }, new { opensAt = "14:00", closesAt = mondayClose } }
                : []
        }).ToArray(),
        concurrencyToken
    };

    private static object EveryDayBody(string? concurrencyToken = null, string? closed = null) => new
    {
        days = Enum.GetNames<DayOfWeek>().Select(day => new
        {
            dayOfWeek = day.ToUpperInvariant(),
            intervals = day.ToUpperInvariant() == closed
                ? Array.Empty<object>()
                : new object[] { new { opensAt = "08:00", closesAt = "18:00" } }
        }).ToArray(),
        concurrencyToken
    };

    private sealed record ErrorPayload(string Code, string Message);
    private sealed record SchedulePayload(bool Configured, IReadOnlyList<ScheduleDayPayload> Days, string? ConcurrencyToken);
    private sealed record ScheduleDayPayload(string DayOfWeek, IReadOnlyList<ScheduleIntervalPayload> Intervals);
    private sealed record ScheduleIntervalPayload(string OpensAt, string ClosesAt);
    private sealed record RoomBlockPage(IReadOnlyList<RoomBlockPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record RoomBlockPayload(Guid Id, Guid RoomId, string Status, string ConcurrencyToken);
}
