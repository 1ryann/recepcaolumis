using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

/// <summary>Operational WhatsApp opt-in: capture, withdrawal, audit and the send-time gate (docs/operations/whatsapp-consent.md).</summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class WhatsAppOptInApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    // ---- the send-time gate -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_customer_who_never_opted_in_receives_nothing_and_no_reschedule_link_is_issued()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: null);
        var originalHash = await CancelForIncidentAsync(seed.ReservationId);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("RECIPIENT_NOT_OPTED_IN", notice.LastErrorCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(originalHash, (await db.RescheduleTokens.AsNoTracking().SingleAsync(x => x.ReservationId == seed.ReservationId)).TokenHash);
        Assert.False(await db.AuditEntries.AnyAsync(x => x.Action == "RESCHEDULE_LINK_ISSUED"));
    }

    [Fact]
    public async Task A_professional_who_never_opted_in_is_not_messaged_about_arrivals()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: true, professionalOptIn: false);
        await CheckInAsync(seed);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("RECIPIENT_NOT_OPTED_IN", notice.LastErrorCode);
    }

    [Fact]
    public async Task A_withdrawal_made_after_the_event_was_queued_is_honoured_at_send_time()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: true);
        await QueueConfirmationAsync(seed.ReservationId);
        await LoginManagerAsync();

        var response = await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-out", new { phone = "(69) 98111-0001" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new OptOutPayload(1, 1), await response.Content.ReadFromJsonAsync<OptOutPayload>());   // both records share the number

        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("RECIPIENT_OPTED_OUT", notice.LastErrorCode);
    }

    [Fact]
    public async Task A_customer_who_opted_in_is_messaged()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: true);
        await QueueConfirmationAsync(seed.ReservationId);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
        Assert.Equal(WhatsAppNotificationStatus.Accepted, Assert.Single(await factory.NotificationsAsync()).Status);
    }

    // ---- capture --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Registration_records_the_opt_in_only_when_ticked_and_audits_it_without_the_number()
    {
        await factory.ResetAsync();

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("(69) 98222-0001", optIn: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("(69) 98222-0002", optIn: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("(69) 98222-0003", optIn: false)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ticked = await db.Customers.AsNoTracking().SingleAsync(x => x.NormalizedPhone == "+5569982220001");
        Assert.Equal(WhatsAppOptInStatus.Granted, ticked.WhatsAppOptIn.Status);
        Assert.Equal(WhatsAppOptInSource.CustomerRegistration, ticked.WhatsAppOptIn.Source);
        Assert.Equal(WhatsAppOptInState.CurrentTextVersion, ticked.WhatsAppOptIn.TextVersion);
        Assert.Equal(factory.UtcNow, ticked.WhatsAppOptIn.ChangedAt);
        foreach (var phone in new[] { "+5569982220002", "+5569982220003" })
            Assert.Equal(WhatsAppOptInStatus.NotRecorded,
                (await db.Customers.AsNoTracking().SingleAsync(x => x.NormalizedPhone == phone)).WhatsAppOptIn.Status);

        var audit = Assert.Single(await db.AuditEntries.AsNoTracking().Where(x => x.Action == "WHATSAPP_OPT_IN_GRANTED").ToListAsync());
        Assert.Equal("CUSTOMER", audit.TargetEntityType);
        Assert.Equal(ticked.Id, audit.TargetEntityId);
        Assert.Equal(ticked.ApplicationUserId, audit.TargetUserId);
        Assert.DoesNotContain("982220001", System.Text.Json.JsonSerializer.Serialize(audit));
    }

    [Fact]
    public async Task Assisted_booking_records_a_ticked_opt_in_as_collected_by_the_reception()
    {
        await factory.ResetAsync();
        PinClockToMorning();
        var seed = await SeedAsync(customerOptIn: null);
        await LoginManagerAsync();
        var start = factory.UtcNow.AddHours(3);

        var response = await factory.PostWithCsrfAsync("/api/reception/reservations", new
        {
            name = "Cliente Opt-in", phone = "(69) 98111-0001", professionalId = seed.ProfessionalId,
            startAt = start, endAt = start.AddHours(1), whatsAppOptIn = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var customer = await CustomerAsync(seed.CustomerId);
        Assert.Equal(WhatsAppOptInStatus.Granted, customer.WhatsAppOptIn.Status);
        Assert.Equal(WhatsAppOptInSource.Reception, customer.WhatsAppOptIn.Source);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.AsNoTracking()
            .SingleAsync(x => x.Action == "WHATSAPP_OPT_IN_GRANTED");
        Assert.NotNull(audit.ActorUserId);                                   // the receptionist who recorded it
        Assert.Equal(customer.Id, audit.TargetEntityId);
    }

    [Fact]
    public async Task The_public_kiosk_cannot_record_an_opt_in_because_anyone_could_type_anyone_s_number()
    {
        await factory.ResetAsync();
        PinClockToMorning();
        var seed = await SeedAsync(customerOptIn: null);
        var start = factory.UtcNow.AddHours(3);

        var ticked = await factory.Client.PostAsJsonAsync("/api/totem/reservations", new
        {
            name = "Cliente Totem", phone = "(69) 98333-0002", professionalId = seed.ProfessionalId,
            startAt = start, endAt = start.AddHours(1), whatsAppOptIn = true
        });
        var unticked = await factory.Client.PostAsJsonAsync("/api/totem/reservations", new
        {
            name = "Cliente Totem", phone = "(69) 98333-0001", professionalId = seed.ProfessionalId,
            startAt = start.AddHours(2), endAt = start.AddHours(3)
        });

        Assert.Equal(HttpStatusCode.BadRequest, ticked.StatusCode);
        Assert.Equal("WHATSAPP_OPT_IN_NOT_AVAILABLE_HERE", (await ticked.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Equal(HttpStatusCode.OK, unticked.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Customers.AnyAsync(x => x.NormalizedPhone == "+5569983330002"));   // nothing was created
        Assert.Equal(WhatsAppOptInStatus.NotRecorded,
            (await db.Customers.AsNoTracking().SingleAsync(x => x.NormalizedPhone == "+5569983330001")).WhatsAppOptIn.Status);
        Assert.False(await db.AuditEntries.AnyAsync(x => x.Action == "WHATSAPP_OPT_IN_GRANTED"));
    }

    [Fact]
    public async Task A_booking_that_started_at_the_totem_records_the_opt_in_ticked_on_the_phone_as_totem()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: null);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("(69) 98555-0001", optIn: null, email: "qr@lumis.test")).StatusCode);
        var handoff = (await (await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs",
            new { professionalId = seed.ProfessionalId })).Content.ReadFromJsonAsync<HandoffPayload>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("qr@lumis.test", Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim",
            new { handoffToken = handoff.HandoffToken })).StatusCode);
        var start = TomorrowAtTenInPortoVelho();

        var booked = await factory.PostWithCsrfAsync("/api/customer/reservations", new
        {
            professionalId = seed.ProfessionalId, startAt = start, endAt = start.AddHours(1),
            handoffToken = handoff.HandoffToken, whatsAppOptIn = true
        });

        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var optIn = (await factory.Client.GetFromJsonAsync<OptInPayload>("/api/customer/me/whatsapp-opt-in"))!;
        Assert.Equal("GRANTED", optIn.Status);
        Assert.Equal("TOTEM", optIn.Source);
    }

    [Fact]
    public async Task A_portal_booking_records_a_ticked_opt_in_as_customer_portal_and_an_unticked_one_changes_nothing()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: null);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("(69) 98555-0002", optIn: null, email: "portal@lumis.test")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("portal@lumis.test", Password)).StatusCode);
        var start = TomorrowAtTenInPortoVelho();

        Assert.Equal(HttpStatusCode.Created, (await factory.PostWithCsrfAsync("/api/customer/reservations",
            new { professionalId = seed.ProfessionalId, startAt = start, endAt = start.AddHours(1) })).StatusCode);
        Assert.Equal("NOT_RECORDED", (await factory.Client.GetFromJsonAsync<OptInPayload>("/api/customer/me/whatsapp-opt-in"))!.Status);

        Assert.Equal(HttpStatusCode.Created, (await factory.PostWithCsrfAsync("/api/customer/reservations",
            new { professionalId = seed.ProfessionalId, startAt = start.AddHours(2), endAt = start.AddHours(3), whatsAppOptIn = true })).StatusCode);
        var optIn = (await factory.Client.GetFromJsonAsync<OptInPayload>("/api/customer/me/whatsapp-opt-in"))!;
        Assert.Equal("GRANTED", optIn.Status);
        Assert.Equal("CUSTOMER_PORTAL", optIn.Source);
    }

    [Fact]
    public async Task The_reception_looks_a_number_up_with_masked_names_and_records_an_in_person_opt_in_for_customers_only()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(customerOptIn: null, professionalOptIn: false);
        await LoginManagerAsync();

        var before = (await (await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-in/lookup",
            new { phone = "(69) 98111-0001" })).Content.ReadFromJsonAsync<LookupPayload>())!;
        Assert.Equal(2, before.Records.Count);
        var customerRecord = Assert.Single(before.Records, x => x.Kind == "CUSTOMER");
        Assert.Equal("Cliente O.", customerRecord.MaskedName);                // never the full name
        Assert.False(customerRecord.HasAccount);
        Assert.Equal("NOT_RECORDED", customerRecord.OptIn.Status);

        var granted = await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-in", new { phone = "(69) 98111-0001" });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(1, (await granted.Content.ReadFromJsonAsync<GrantPayload>())!.Customers);

        var customer = await CustomerAsync(seed.CustomerId);
        Assert.Equal(WhatsAppOptInSource.Reception, customer.WhatsAppOptIn.Source);
        Assert.True(customer.WhatsAppOptIn.IsGranted);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await db.Professionals.AsNoTracking().SingleAsync(x => x.Id == seed.ProfessionalId)).WhatsAppOptIn.IsGranted);
        Assert.NotNull((await db.AuditEntries.AsNoTracking().SingleAsync(x => x.Action == "WHATSAPP_OPT_IN_GRANTED")).ActorUserId);

        Assert.Equal(HttpStatusCode.NotFound, (await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-in",
            new { phone = "(69) 98999-0000" })).StatusCode);
    }

    [Fact]
    public async Task A_booking_never_withdraws_an_opt_in_that_exists()
    {
        await factory.ResetAsync();
        PinClockToMorning();
        var seed = await SeedAsync(customerOptIn: true);
        var start = factory.UtcNow.AddHours(3);

        var response = await factory.Client.PostAsJsonAsync("/api/totem/reservations", new
        {
            name = "Cliente", phone = "(69) 98111-0001", professionalId = seed.ProfessionalId,
            startAt = start, endAt = start.AddHours(1), whatsAppOptIn = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await CustomerAsync(seed.CustomerId)).WhatsAppOptIn.IsGranted);
    }

    // ---- self-service -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_customer_sees_grants_and_withdraws_their_own_opt_in()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("(69) 98444-0001", optIn: null, email: "self@lumis.test")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("self@lumis.test", Password)).StatusCode);

        Assert.Equal("NOT_RECORDED", (await factory.Client.GetFromJsonAsync<OptInPayload>("/api/customer/me/whatsapp-opt-in"))!.Status);
        var granted = await (await factory.PutWithCsrfAsync("/api/customer/me/whatsapp-opt-in", new { optIn = true }))
            .Content.ReadFromJsonAsync<OptInPayload>();
        Assert.Equal(new OptInPayload("GRANTED", factory.UtcNow, "CUSTOMER_PORTAL", WhatsAppOptInState.CurrentTextVersion), granted);
        var revoked = await (await factory.PutWithCsrfAsync("/api/customer/me/whatsapp-opt-in", new { optIn = false }))
            .Content.ReadFromJsonAsync<OptInPayload>();
        Assert.Equal("REVOKED", revoked!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var actions = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.AsNoTracking()
            .Where(x => x.Action.StartsWith("WHATSAPP_OPT_IN_")).OrderBy(x => x.OccurredAt).ThenBy(x => x.Action)
            .Select(x => x.Action).ToListAsync();
        Assert.Equal(["WHATSAPP_OPT_IN_GRANTED", "WHATSAPP_OPT_IN_REVOKED"], actions.Order());
    }

    [Fact]
    public async Task A_professional_grants_their_own_opt_in_and_a_new_number_needs_a_new_one()
    {
        await factory.ResetAsync();
        var user = await factory.CreateUserAsync($"prof-optin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Profissional]);
        var seed = await SeedAsync(customerOptIn: true, professionalOptIn: false, professionalUserId: user.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);

        var granted = await (await factory.PutWithCsrfAsync("/api/professional/me/whatsapp-opt-in", new { optIn = true }))
            .Content.ReadFromJsonAsync<OptInPayload>();
        Assert.Equal("GRANTED", granted!.Status);
        Assert.Equal("PROFESSIONAL_PORTAL", granted.Source);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var professional = await db.Professionals.SingleAsync(x => x.Id == seed.ProfessionalId);
            professional.Update(professional.Name, professional.Profession, "+5569977770000", factory.UtcNow);
            await db.SaveChangesAsync();
        }

        Assert.Equal("NOT_RECORDED", (await factory.Client.GetFromJsonAsync<OptInPayload>("/api/professional/me/whatsapp-opt-in"))!.Status);
    }

    [Fact]
    public async Task Only_staff_can_record_a_withdrawal_by_number_and_an_invalid_number_is_rejected()
    {
        await factory.ResetAsync();
        await SeedAsync(customerOptIn: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-out", new { phone = "(69) 98111-0001" })).StatusCode);

        await LoginManagerAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-out", new { phone = "123" })).StatusCode);
        Assert.Equal(new OptOutPayload(1, 1), await (await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-out",
            new { phone = "(69) 98111-0001" })).Content.ReadFromJsonAsync<OptOutPayload>());   // same number on both records
        Assert.Equal(new OptOutPayload(0, 0), await (await factory.PostWithCsrfAsync("/api/reception/whatsapp-opt-out",
            new { phone = "(69) 98111-0001" })).Content.ReadFromJsonAsync<OptOutPayload>());   // already withdrawn
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private sealed record Seed(Guid ReservationId, Guid ProfessionalId, Guid RoomId, Guid CustomerId);
    private sealed record OptInPayload(string Status, DateTimeOffset? ChangedAt, string? Source, string? TextVersion);
    private sealed record OptOutPayload(int Customers, int Professionals);
    private sealed record GrantPayload(int Customers);
    private sealed record LookupRecordPayload(Guid Id, string Kind, string MaskedName, bool HasAccount, bool IsActive, OptInPayload OptIn);
    private sealed record LookupPayload(IReadOnlyList<LookupRecordPayload> Records);
    private sealed record HandoffPayload(Guid Id, string HandoffToken, string StatusToken);
    private sealed record ErrorPayload(string Code, string Message);

    /// <summary>14:00Z tomorrow = 10:00 in Porto Velho: inside the default operating hours, same local day as its end.</summary>
    private DateTimeOffset TomorrowAtTenInPortoVelho() =>
        new DateTimeOffset(factory.UtcNow.UtcDateTime.Date.AddDays(1), TimeSpan.Zero).AddHours(14);

    /// <param name="customerOptIn">true granted, false revoked, null never recorded.</param>
    private async Task<Seed> SeedAsync(bool? customerOptIn, bool professionalOptIn = true, string? professionalUserId = null)
    {
        await factory.SeedDefaultOperatingHoursAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        // Professional and customer share the number only to exercise the by-number withdrawal on both records.
        var professional = Professional.Create("Dra. Opt-in", "Psicologia", "+5569981110001", now);
        if (professionalUserId is not null) professional.LinkUser(professionalUserId, now);
        if (professionalOptIn) professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now);
        var customer = Customer.Create("Cliente Opt-in", "+5569981110001", now);
        if (customerOptIn == true) customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);
        if (customerOptIn == false) customer.RevokeWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, now);
        var room = Room.Create($"Sala Opt-in {Guid.NewGuid():N}"[..30], null, 10, 50, now);
        var start = now.AddHours(2);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1), "seed", now.AddDays(-1), customer.Id);
        db.AddRange(professional, customer, room, reservation);
        await db.SaveChangesAsync();
        return new Seed(reservation.Id, professional.Id, room.Id, customer.Id);
    }

    private async Task<byte[]> CancelForIncidentAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.SingleAsync(x => x.Id == reservationId);
        reservation.Cancel("PROFESSIONAL_INCIDENT", factory.UtcNow, ReservationCancellationReason.ProfessionalUnavailable);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        db.RescheduleTokens.Add(RescheduleToken.Create(reservation.Id, hash, factory.UtcNow, factory.UtcNow.AddHours(48)));
        db.WhatsAppNotifications.Add(WhatsAppNotification.ReservationCancelled(reservation, factory.UtcNow)!);
        await db.SaveChangesAsync();
        return hash;
    }

    private async Task CheckInAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = Visit.Arrive(seed.ProfessionalId, seed.RoomId, seed.ReservationId, "Cliente Opt-in", "TOTEM", factory.UtcNow, seed.CustomerId);
        db.Visits.Add(visit);
        db.WhatsAppNotifications.Add(WhatsAppNotification.ClientCheckedIn(visit, factory.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task QueueConfirmationAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservationId);
        db.WhatsAppNotifications.Add(WhatsAppNotification.AppointmentConfirmed(reservation, factory.UtcNow)!);
        await db.SaveChangesAsync();
    }

    private async Task<Customer> CustomerAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Customers.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private Task<HttpResponseMessage> RegisterAsync(string phone, bool? optIn, string? email = null)
    {
        email ??= $"optin-{Guid.NewGuid():N}@lumis.test";
        object body = optIn is null
            ? new { name = "Cliente Cadastro", phone, email, password = Password, confirmation = Password }
            : new { name = "Cliente Cadastro", phone, email, password = Password, confirmation = Password, whatsAppOptIn = optIn };
        return factory.PostWithCsrfAsync("/api/customer/register", body);
    }

    private async Task LoginManagerAsync()
    {
        var manager = await factory.CreateUserAsync($"optin-manager-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(manager.Email!, Password)).StatusCode);
    }

    /// <summary>Mid-morning in Porto Velho, so a booking window never straddles civil midnight.</summary>
    private void PinClockToMorning()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(factory.UtcNow, zone).DateTime);
        factory.FreezeTime(new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(localToday.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified), zone), TimeSpan.Zero));
    }
}
