using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ReschedulingApiTests(ModulesApiFactory factory)
{
    private DateOnly Date => DateOnly.FromDateTime(factory.UtcNow.UtcDateTime).AddDays(30);

    [Fact]
    public async Task Resolve_returns_the_reschedule_context_without_pii()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync();

        var response = await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = seed.Token });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ResolvePayload>();

        Assert.Equal(seed.Professional.Id, payload!.ProfessionalId);
        Assert.Equal(seed.Professional.Name, payload.ProfessionalName);
        Assert.Equal(60, payload.DurationMinutes);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(seed.Customer.NormalizedPhone, body);
        Assert.DoesNotContain("customerId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_with_an_expired_token_is_generic()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync(expired: true);

        var response = await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = seed.Token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_RESCHEDULE_LINK", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Slots_match_the_central_availability_engine()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync();

        var slots = await factory.Client.GetFromJsonAsync<SlotPayload[]>(
            $"/api/reschedule/slots?token={Uri.EscapeDataString(seed.Token)}&date={Date:yyyy-MM-dd}");

        await using var scope = factory.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IAppointmentAvailabilityService>();
        var expected = await engine.FindSlotsAsync(seed.Professional.Id, Date, 60, default);
        Assert.Equal(expected.Select(x => x.StartAt).OrderBy(x => x),
            slots!.Select(x => x.StartAt).OrderBy(x => x));
        Assert.NotEmpty(slots!);
    }

    [Fact]
    public async Task Confirm_creates_an_approved_replacement_and_consumes_the_token()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync();
        var startAt = Utc(Date, new TimeOnly(14, 0));
        var endAt = Utc(Date, new TimeOnly(15, 0));

        var response = await factory.Client.PostAsJsonAsync("/api/reschedule/confirm",
            new { token = seed.Token, startAt, endAt });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ConfirmPayload>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var replacement = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == payload!.ReservationId);
        Assert.Equal(ReservationKind.Reschedule, replacement.Kind);
        Assert.Equal(ReservationStatus.Approved, replacement.Status);
        Assert.Equal(seed.Reservation.Id, replacement.OriginalReservationId);
        Assert.Equal(ReservationStatus.Cancelled,
            (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == seed.Reservation.Id)).Status);
        Assert.NotNull((await db.RescheduleTokens.AsNoTracking()
            .SingleAsync(x => x.ReservationId == seed.Reservation.Id)).UsedAt);
    }

    [Fact]
    public async Task Second_confirm_with_the_same_token_is_rejected()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync();
        var startAt = Utc(Date, new TimeOnly(14, 0));
        var endAt = Utc(Date, new TimeOnly(15, 0));

        (await factory.Client.PostAsJsonAsync("/api/reschedule/confirm", new { token = seed.Token, startAt, endAt }))
            .EnsureSuccessStatusCode();
        var second = await factory.Client.PostAsJsonAsync("/api/reschedule/confirm",
            new { token = seed.Token, startAt = Utc(Date, new TimeOnly(15, 0)), endAt = Utc(Date, new TimeOnly(16, 0)) });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync(x => x.OriginalReservationId == seed.Reservation.Id));
    }

    [Fact]
    public async Task Confirm_on_a_conflicting_slot_is_a_conflict_and_keeps_the_token()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync();
        var startAt = Utc(Date, new TimeOnly(14, 0));
        var endAt = Utc(Date, new TimeOnly(15, 0));
        Guid blockerId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var blocker = Reservation.CreateApproved(seed.Room.Id, seed.Professional.Id, startAt, endAt,
                "seed", factory.UtcNow);
            db.Reservations.Add(blocker);
            await db.SaveChangesAsync();
            blockerId = blocker.Id;
        }

        var conflict = await factory.Client.PostAsJsonAsync("/api/reschedule/confirm",
            new { token = seed.Token, startAt, endAt });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Reservations.Remove(await db.Reservations.SingleAsync(x => x.Id == blockerId));
            await db.SaveChangesAsync();
        }

        var retry = await factory.Client.PostAsJsonAsync("/api/reschedule/confirm",
            new { token = seed.Token, startAt, endAt });
        retry.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Totem_created_customer_reschedules_without_login()
    {
        await factory.ResetAsync();
        var seed = await SeedCancelledReservationAsync(totemCustomer: true);
        var startAt = Utc(Date, new TimeOnly(9, 0));
        var endAt = Utc(Date, new TimeOnly(10, 0));

        var response = await factory.Client.PostAsJsonAsync("/api/reschedule/confirm",
            new { token = seed.Token, startAt, endAt });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("items", body, StringComparison.OrdinalIgnoreCase);
    }

    // 14:00 in America/Porto_Velho. The seed opens 08:00–18:00, so a 10:00 slot the same day is inside the operating
    // hours and has already started: only the clock can turn it away. The link arrives by WhatsApp and may be opened
    // hours later, which is exactly when the morning's slots have gone.
    private static readonly DateTimeOffset Afternoon = new(2026, 1, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TestDay = new(2026, 1, 15);

    [Fact]
    public async Task The_link_never_offers_a_slot_that_has_already_started()
    {
        await factory.ResetAsync();
        factory.FreezeTime(Afternoon);
        var seed = await SeedCancelledReservationAsync();

        var slots = await factory.Client.GetFromJsonAsync<SlotPayload[]>(
            $"/api/reschedule/slots?token={Uri.EscapeDataString(seed.Token)}&date={TestDay:yyyy-MM-dd}");

        Assert.NotEmpty(slots!); // 14:15 onwards is still open
        Assert.All(slots!, slot => Assert.True(slot.StartAt > Afternoon, $"{slot.StartAt:O} has already started"));
    }

    [Fact]
    public async Task The_link_rejects_a_slot_that_has_already_started_and_keeps_the_token_usable()
    {
        await factory.ResetAsync();
        factory.FreezeTime(Afternoon);
        var seed = await SeedCancelledReservationAsync();
        var startAt = Utc(TestDay, new TimeOnly(10, 0));

        var response = await factory.Client.PostAsJsonAsync("/api/reschedule/confirm",
            new { token = seed.Token, startAt, endAt = startAt.AddHours(1) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SLOT_IN_THE_PAST", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // A refused slot must not burn the single-use link: the customer picks another time with the same button.
        Assert.Null((await db.RescheduleTokens.AsNoTracking().SingleAsync(x => x.ReservationId == seed.Reservation.Id)).UsedAt);
    }

    private async Task<Seed> SeedCancelledReservationAsync(bool expired = false, bool totemCustomer = false)
    {
        var now = factory.UtcNow;
        var schedule = OperatingHoursSchedule.Create(now);
        var professional = Professional.Create("Reagendar API", "Fisioterapia",
            $"699{Random.Shared.Next(10000000, 99999999)}", now);
        var room = Room.Create($"Sala reagendar {Guid.NewGuid():N}", null, 4, 90m, now);
        var customer = totemCustomer
            ? Customer.Create("Cliente Totem", "69999990001", now)
            : Customer.Create("Cliente Conta", "69999990002", now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id,
            Utc(Date, new TimeOnly(10, 0)), Utc(Date, new TimeOnly(11, 0)), "seed", now, customer.Id);
        reservation.Cancel("PROFESSIONAL_INCIDENT", now, ReservationCancellationReason.ProfessionalUnavailable);

        var raw = RandomNumberGenerator.GetBytes(32);
        var token = expired
            ? RescheduleToken.Create(reservation.Id, SHA256.HashData(raw), now.AddHours(-49), now.AddHours(-1))
            : RescheduleToken.Create(reservation.Id, SHA256.HashData(raw), now, now.AddHours(48));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.OperatingHoursSchedules.Add(schedule);
        foreach (var day in Enum.GetValues<DayOfWeek>())
            db.OperatingHourIntervals.AddRange(OperatingHourInterval.CreateDay(schedule.Id, day,
                [new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(18, 0))]));
        db.AddRange(professional, room, customer, reservation);
        db.RescheduleTokens.Add(token);
        await db.SaveChangesAsync();
        return new Seed(professional, room, customer, reservation, WebEncoders.Base64UrlEncode(raw));
    }

    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")));

    private sealed record Seed(Professional Professional, Room Room, Customer Customer,
        Reservation Reservation, string Token);
    private sealed record ResolvePayload(Guid ProfessionalId, string ProfessionalName,
        DateTimeOffset OriginalStartAt, DateTimeOffset OriginalEndAt, int DurationMinutes, DateTimeOffset ExpiresAt);
    private sealed record SlotPayload(DateTimeOffset StartAt, DateTimeOffset EndAt);
    private sealed record ConfirmPayload(Guid ReservationId, DateTimeOffset StartAt, DateTimeOffset EndAt,
        string ProfessionalName, string RoomName);
    private sealed record ErrorPayload(string Code, string Message);
}
