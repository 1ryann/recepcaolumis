using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// <c>POST /api/customer/booking-handoffs/resolve</c> (Task 13): a <see cref="IdentityConfiguration"/>
/// CustomerPolicy read-only endpoint that returns just the professional context of a handoff so the
/// phone's booking screen can pre-select the professional. It must never mutate the handoff and must
/// never echo customer data, reservation data, or the token.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class CustomerHandoffApiTests(ModulesApiFactory factory)
{
    private const string Path = "/api/customer/booking-handoffs/resolve";

    [Fact]
    public async Task Resolve_returns_professional_context_for_an_authenticated_customer()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadFromJsonAsync<ResolveBody>();
        Assert.Equal(b.Id, body!.HandoffId);
        Assert.Equal(prof, body.ProfessionalId);
        Assert.Equal("Dra. Ana", body.ProfessionalName);
        Assert.Equal("Fisioterapia", body.Profession);
        Assert.Equal(b.ExpiresAt, body.ExpiresAt);

        // No customer data, no reservation data, no token echoed back.
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain(b.HandoffToken, raw);
        Assert.DoesNotContain(b.StatusToken, raw);
        Assert.DoesNotContain(seed.CustomerName, raw);
        Assert.DoesNotContain(seed.CustomerPhone, raw);
        Assert.DoesNotContain("reservation", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_on_an_expired_handoff_is_410()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(DateTimeOffset.UtcNow);
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        factory.FreezeTime(factory.UtcNow.AddMinutes(6));   // past the initial 5-minute window
        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Gone, r.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await r.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Resolve_on_a_non_pending_handoff_is_410()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);

        // Cancel folds the row to Expired while it is still inside the time window.
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Gone, r.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await r.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Resolve_with_a_bad_token_is_400()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await r.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Resolve_requires_an_authenticated_customer()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);

        // No login: CustomerPolicy on the group rejects the anonymous caller before the handler runs.
        var r = await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Resolve_never_mutates_the_handoff()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var (startedBefore, expiresBefore, statusBefore) = await ReadHandoffAsync(b.Id);
        Assert.Null(startedBefore);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await factory.PostWithCsrfAsync(Path, new { handoffToken = b.HandoffToken })).StatusCode);

        var (startedAfter, expiresAfter, statusAfter) = await ReadHandoffAsync(b.Id);
        Assert.Null(startedAfter);
        Assert.Equal(startedBefore, startedAfter);
        Assert.Equal(expiresBefore, expiresAfter);
        Assert.Equal(statusBefore, statusAfter);
    }

    // ---------------------------------------------------------------------------------------------
    // Task 14 — POST /api/customer/reservations with an optional handoffToken (spec 9.6 CASE 1-4).
    // The reservation and the handoff completion commit together or not at all, and a network retry
    // of the very same request by the very same customer gets the very same reservation back.
    // ---------------------------------------------------------------------------------------------

    private const string CreatePath = "/api/customer/reservations";

    [Fact]
    public async Task First_call_creates_one_reservation_and_completes_the_handoff()
    {
        var a = await ArrangeBookableHandoffAsync();
        var res = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var reservation = await res.Content.ReadFromJsonAsync<ReservationPayload>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync());
        var handoff = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync();
        Assert.Equal(TotemBookingHandoffStatus.Completed, handoff.Status);
        Assert.Equal(reservation!.Id, handoff.ReservationId);
        Assert.NotNull(handoff.CompletedAt);
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "RESERVATION_CREATED"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
    }

    [Fact]
    public async Task Retry_by_the_same_customer_returns_the_same_reservation_without_duplicating()
    {
        var a = await ArrangeBookableHandoffAsync();
        var first = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<ReservationPayload>())!.Id;

        var retry = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);                    // 200, not 201, not 409
        Assert.Equal(firstId, (await retry.Content.ReadFromJsonAsync<ReservationPayload>())!.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync());                  // count stays 1
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "RESERVATION_CREATED"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
    }

    [Fact]
    public async Task Another_customer_cannot_recover_the_reservation_through_the_handoff()
    {
        var a = await ArrangeBookableHandoffAsync();
        Assert.Equal(HttpStatusCode.Created, (await factory.PostWithCsrfAsync(CreatePath, Body(a))).StatusCode);

        var intruder = await SeedCustomerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(intruder.Email, intruder.Password)).StatusCode);
        var res = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("HANDOFF_ALREADY_USED", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Generic body: nothing about the reservation the first customer owns.
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain(a.ProfessionalId.ToString(), text);
        Assert.DoesNotContain(a.Token, text);
        Assert.DoesNotContain("reservationId", text, StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Replay_is_409_when_the_completed_handoffs_reservation_is_no_longer_approved()
    {
        var a = await ArrangeBookableHandoffAsync();
        var created = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reservationId = (await created.Content.ReadFromJsonAsync<ReservationPayload>())!.Id;

        // The booking is cancelled AFTER the handoff was completed; the invite must not resurrect it.
        await using (var mutate = factory.Services.CreateAsyncScope())
        {
            var mdb = mutate.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await mdb.Reservations.SingleAsync(x => x.Id == reservationId);
            reservation.Cancel(reservation.RequestedByUserId, DateTimeOffset.UtcNow);
            await mdb.SaveChangesAsync();
        }

        var res = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("HANDOFF_ALREADY_USED", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Generic body: no reservation id, no token.
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain(reservationId.ToString(), text);
        Assert.DoesNotContain(a.Token, text);
        Assert.DoesNotContain("reservationId", text, StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync());   // no second row
    }

    [Fact]
    public async Task Concurrent_duplicate_creates_exactly_one_reservation()
    {
        var a = await ArrangeBookableHandoffAsync();
        var t1 = factory.PostWithCsrfAsync(CreatePath, Body(a));
        var t2 = factory.PostWithCsrfAsync(CreatePath, Body(a));
        var results = await Task.WhenAll(t1, t2);

        var codes = results.Select(r => (int)r.StatusCode).OrderBy(x => x).ToArray();
        Assert.Contains(201, codes);
        Assert.All(codes, c => Assert.True(c is 200 or 201, $"unexpected status {c}"));

        var ids = new List<Guid>();
        foreach (var r in results) ids.Add((await r.Content.ReadFromJsonAsync<ReservationPayload>())!.Id);
        Assert.Single(ids.Distinct());                                        // both name the SAME reservation

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync());                  // exactly one row
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "RESERVATION_CREATED"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
        var handoff = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync();
        Assert.Equal(TotemBookingHandoffStatus.Completed, handoff.Status);
        Assert.Equal(ids[0], handoff.ReservationId);
    }

    [Fact]
    public async Task Handoff_present_with_a_genuine_slot_conflict_returns_conflict_and_leaves_the_handoff_pending()
    {
        var a = await ArrangeBookableHandoffAsync();

        // A different customer books professional P at the very same slot through the normal path
        // (no handoffToken), so P is genuinely busy when customer A tries to complete the handoff.
        // This is NOT a concurrent duplicate of A's handoff — it is an unrelated occupant.
        var other = await SeedCustomerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(other.Email, other.Password)).StatusCode);
        var blocking = await factory.PostWithCsrfAsync(CreatePath,
            new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1) });
        Assert.Equal(HttpStatusCode.Created, blocking.StatusCode);

        // Customer A now completes the handoff against the now-occupied slot: rollback + ChangeTracker
        // clear + re-read (still Pending, no concurrent winner) -> falls through to the normal
        // availability-conflict response, and the handoff must be left Pending with nothing committed.
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(a.Email, a.Password)).StatusCode);
        var res = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("RESERVATION_RESOURCE_CONFLICT", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handoff = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync();
            Assert.Equal(TotemBookingHandoffStatus.Pending, handoff.Status);
            Assert.Null(handoff.ReservationId);
            Assert.Null(handoff.CompletedAt);
            Assert.Equal(1, await db.Reservations.CountAsync());   // only the other customer's booking
            Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
        }

        // The handoff survived the earlier conflict and is still usable: retry against a free slot.
        var retry = await factory.PostWithCsrfAsync(CreatePath,
            new { professionalId = a.ProfessionalId, startAt = a.Start.AddHours(2), endAt = a.Start.AddHours(3), handoffToken = a.Token });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handoff = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync();
            Assert.Equal(TotemBookingHandoffStatus.Completed, handoff.Status);
            Assert.NotNull(handoff.ReservationId);
            Assert.NotNull(handoff.CompletedAt);
            Assert.Equal(2, await db.Reservations.CountAsync());
            Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
        }
    }

    [Fact]
    public async Task Reservation_without_handoffToken_is_unchanged()
    {
        var a = await ArrangeBookableHandoffAsync();
        var res = await factory.PostWithCsrfAsync(CreatePath,
            new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1) });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync());
        // The handoff arranged above is untouched: still Pending, still unlinked.
        var handoff = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync();
        Assert.Equal(TotemBookingHandoffStatus.Pending, handoff.Status);
        Assert.Null(handoff.ReservationId);
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
    }

    [Fact]
    public async Task Creating_with_an_expired_handoff_is_410()
    {
        var a = await ArrangeBookableHandoffAsync();
        // ResetAsync (inside the arrange) unfreezes, so pin the clock only afterwards.
        factory.FreezeTime(DateTimeOffset.UtcNow.AddMinutes(21));   // past claim's grace + hard ceiling

        var res = await factory.PostWithCsrfAsync(CreatePath, Body(a));
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        await AssertNoReservationAsync();
    }

    [Fact]
    public async Task Creating_with_a_handoff_for_another_professional_is_400()
    {
        var a = await ArrangeBookableHandoffAsync();
        var other = await SeedActiveProfessionalWithRoomAsync();

        var res = await factory.PostWithCsrfAsync(CreatePath,
            new { professionalId = other, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = a.Token });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        await AssertNoReservationAsync();
    }

    [Fact]
    public async Task Creating_with_an_unknown_or_malformed_handoff_token_is_400()
    {
        var a = await ArrangeBookableHandoffAsync();

        var malformed = await factory.PostWithCsrfAsync(CreatePath,
            new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await malformed.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var unknown = await factory.PostWithCsrfAsync(CreatePath,
            new
            {
                professionalId = a.ProfessionalId,
                startAt = a.Start,
                endAt = a.Start.AddHours(1),
                handoffToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[32])
            });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await unknown.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await AssertNoReservationAsync();
    }

    private static object Body(BookableHandoff a) =>
        new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = a.Token };

    private async Task AssertNoReservationAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Reservations.CountAsync());
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "RESERVATION_CREATED"));
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
    }

    /// <summary>
    /// A logged-in customer whose phone has opened the QR (claim) for a Pending handoff on an active
    /// professional that has an active room and an open slot tomorrow morning.
    /// </summary>
    private async Task<BookableHandoff> ArrangeBookableHandoffAsync()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedCustomerAsync();
        var professionalId = await SeedActiveProfessionalWithRoomAsync();
        var b = await CreateHandoffAsync(professionalId);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        // The phone opens the QR link: claim starts the clock and buys the grace window.
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync(
            "/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken })).StatusCode);

        // 14:00Z == 10:00 in America/Porto_Velho (the configured operational zone), well inside the
        // 00:00-23:59 default operating hours and on the same local day as its 15:00Z end.
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero).AddHours(14);
        return new BookableHandoff(seed.Email, seed.Password, professionalId, b.HandoffToken, start);
    }

    private async Task<Guid> SeedActiveProfessionalWithRoomAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var room = Room.Create($"Sala Handoff {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create($"Profissional Handoff {Guid.NewGuid():N}", "Fisioterapia",
            $"659{Random.Shared.Next(10000000, 99999999)}", now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, professional);
        await db.SaveChangesAsync();
        return professional.Id;
    }

    private async Task<(DateTimeOffset? StartedAt, DateTimeOffset ExpiresAt, TotemBookingHandoffStatus Status)> ReadHandoffAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TotemBookingHandoffs.AsNoTracking().SingleAsync(x => x.Id == id);
        return (row.StartedAt, row.ExpiresAt, row.Status);
    }

    private async Task<CustomerSeed> SeedCustomerAsync()
    {
        const string password = "Valid-Password-123!";
        const string name = "Cliente Handoff";
        // Unique per call: UX_Customers_NormalizedPhone is unique, and one Task 14 test seeds two customers.
        var phone = $"+55699{Random.Shared.Next(10_000_000, 99_999_999)}";
        var user = await factory.CreateUserAsync($"handoff-customer-{Guid.NewGuid():N}@lumis.test", password,
            [SystemRoles.Customer], displayName: name);
        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(name, phone, now);
        customer.LinkUser(user.Id, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return new CustomerSeed(user.Email!, password, name, phone);
    }

    private async Task<CreateBody> CreateHandoffAsync(Guid professionalId)
    {
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<CreateBody>())!;
    }

    private sealed record CustomerSeed(string Email, string Password, string CustomerName, string CustomerPhone);
    private sealed record CreateBody(Guid Id, string HandoffToken, string StatusToken, DateTimeOffset ExpiresAt, string ProfessionalName, string Profession);
    private sealed record ResolveBody(Guid HandoffId, Guid ProfessionalId, string ProfessionalName, string Profession, DateTimeOffset ExpiresAt);
    private sealed record ErrorBody(string Code, string Message);

    /// <summary>The seed a <see cref="ArrangeBookableHandoffAsync"/> call hands back to a Task 14 test.</summary>
    private sealed record BookableHandoff(string Email, string Password, Guid ProfessionalId, string Token, DateTimeOffset Start);

    /// <summary>Just the id off a <c>ReservationResponse</c> body — enough to prove "same reservation".</summary>
    private sealed record ReservationPayload(Guid Id);
}
