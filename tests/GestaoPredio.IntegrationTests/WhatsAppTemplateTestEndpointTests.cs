using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Notifications;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Whatsapp;
using static GestaoPredio.IntegrationTests.WhatsAppContractSupport;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// POST /api/admin/whatsapp/template-test: one administrative test notice through the normal pipeline, with the real
/// WhatsAppCloudApiService and the network replaced by <see cref="RecordingGraph"/>. The worker stays disabled.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class WhatsAppTemplateTestEndpointTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private const string ProfessionalPhone = "+5569981240001";
    private const string CustomerPhone = "+5569981240002";
    private const string Path = WhatsappNotificationEndpoints.TemplateTestPath;

    // ---- access ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_an_administrator_can_run_it_a_manager_gets_403_and_anonymous_401()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var anonymous = new Session(host);
        using var manager = new Session(host);
        await manager.LoginAsync(await UserAsync(SystemRoles.Gerente));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(Body("APPOINTMENT_CONFIRMED"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(Body("APPOINTMENT_CONFIRMED"))).StatusCode);
        Assert.Empty(graph.Requests);
        Assert.Empty(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task A_request_without_a_valid_csrf_token_is_rejected_before_anything_happens()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var response = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED"), csrf: "forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CSRF", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Empty(graph.Requests);
        Assert.Empty(await factory.NotificationsAsync());
    }

    [Theory]
    [InlineData("APPOINTMENT_REMINDER")]                                        // prepared, not implemented
    [InlineData("FREE_TEXT")]
    [InlineData("appointment_confirmed_template")]                              // a template name is not a type
    public async Task Only_the_six_implemented_types_are_accepted(string type)
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var response = await admin.PostAsync(Body(type));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_TYPE", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task No_free_text_and_no_template_name_can_be_smuggled_in()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var response = await admin.PostAsync(new
        {
            notificationType = "APPOINTMENT_CONFIRMED", phone = CustomerPhone, templateName = "any_template", body = "texto livre"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);         // unknown members are refused
        Assert.Empty(graph.Requests);
        Assert.Empty(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task A_type_without_a_configured_template_is_refused_and_nothing_is_created()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph, ("Whatsapp:Templates:AppointmentConfirmed", ""));
        using var admin = await AdminAsync(host);

        var response = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("TEMPLATE_NOT_CONFIGURED", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Empty(graph.Requests);
        Assert.Empty(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task An_unknown_number_or_a_person_without_an_eligible_record_is_refused()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var unknown = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED", "+5569981249999"));
        var noRecord = await admin.PostAsync(Body("APPOINTMENT_CANCELLED"));   // nothing of hers is cancelled
        var invalid = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED", "123"));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, noRecord.StatusCode);
        Assert.Equal("NO_ELIGIBLE_RECORD", (await noRecord.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(graph.Requests);
        Assert.Empty(await factory.NotificationsAsync());
    }

    // ---- the opt-in gate is the real one ------------------------------------------------------------------------

    [Theory]
    [InlineData(WhatsAppOptInStatus.NotRecorded, "RECIPIENT_NOT_OPTED_IN")]
    [InlineData(WhatsAppOptInStatus.Revoked, "RECIPIENT_OPTED_OUT")]
    public async Task Without_a_granted_opt_in_the_notice_is_skipped_and_nothing_is_sent(WhatsAppOptInStatus optIn, string code)
    {
        await ArrangeAsync(optIn);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("APPOINTMENT_CONFIRMED");

        Assert.Equal("SKIPPED", result.Status);
        Assert.Equal(code, result.Code);
        Assert.Null(result.MessageId);
        Assert.Empty(graph.Requests);
    }

    // ---- one real-shaped send ----------------------------------------------------------------------------------

    [Fact]
    public async Task With_a_granted_opt_in_one_notice_is_created_processed_and_audited_with_the_exact_template_contract()
    {
        var seed = await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        var adminUser = await UserAsync(SystemRoles.Administrador);
        using var admin = new Session(host);
        await admin.LoginAsync(adminUser);

        var result = await admin.RunAsync("APPOINTMENT_CONFIRMED");

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal("wamid.CONTRACT.1", result.MessageId);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(result.NotificationId, notice.Id);
        Assert.Equal($"TEST:CONFIRM:{seed.ReservationId}", notice.IdempotencyKey);
        Assert.True(notice.IsAdminTest);
        Assert.Equal(1, notice.Attempts);
        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, CustomerPhone, notice);
        AssertTemplate(request, "appointment_confirmed",
            ["Maria", "Dra. Teste Lima", LocalDate(seed.StartAt), LocalTime(seed.StartAt)], button: null);

        var audit = Assert.Single(await AuditsAsync());
        Assert.Equal("WHATSAPP_TEMPLATE_TEST:APPOINTMENT_CONFIRMED", audit.Action);
        Assert.Equal("ACCEPTED", audit.Result);
        Assert.Equal(adminUser.Id, audit.ActorUserId);
        Assert.Equal(notice.Id, audit.TargetEntityId);
        Assert.Equal("wamid.CONTRACT.1", audit.CorrelationId);
        var serialized = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain("981240002", serialized);                       // never the number
        Assert.DoesNotContain("Maria", serialized);                           // never a name or parameter
        Assert.DoesNotContain(AccessToken, serialized);
    }

    [Fact]
    public async Task The_check_in_notice_goes_to_the_professional_from_a_visit_that_is_waiting()
    {
        var seed = await ArrangeAsync(WhatsAppOptInStatus.Granted, startIn: TimeSpan.FromMinutes(10), withWaitingVisit: true);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("CLIENT_CHECKED_IN", ProfessionalPhone);

        Assert.Equal("ACCEPTED", result.Status);
        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, ProfessionalPhone, Assert.Single(await factory.NotificationsAsync()));
        AssertTemplate(request, "professional_client_checked_in", ["Dra. Teste Lima", "Maria", LocalTime(seed.StartAt)], button: null);
    }

    [Fact]
    public async Task A_number_shared_by_two_active_professionals_uses_the_waiting_visit_of_either_instead_of_failing()
    {
        var seed = await ArrangeAsync(WhatsAppOptInStatus.Granted, startIn: TimeSpan.FromMinutes(10), withWaitingVisit: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // WhatsApp is not unique among professionals (the opt-in flow already handles several per number).
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(Professional.Create("Dr. Outro Cadastro", "Psicologia", ProfessionalPhone, factory.UtcNow));
            await db.SaveChangesAsync();
        }
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("CLIENT_CHECKED_IN", ProfessionalPhone);

        Assert.Equal("ACCEPTED", result.Status);
        var notification = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(seed.ProfessionalId, notification.ProfessionalId);
        AssertEnvelope(Assert.Single(graph.Requests), ProfessionalPhone, notification);
    }

    [Fact]
    public async Task The_professional_cancelled_test_keeps_the_existing_reschedule_link_logic()
    {
        var seed = await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var originalHash = await CancelForIncidentAsync(seed);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("PROFESSIONAL_CANCELLED");

        Assert.Equal("ACCEPTED", result.Status);
        var request = Assert.Single(graph.Requests);
        var token = ButtonParameter(request);
        AssertTemplate(request, "client_professional_cancelled",
            ["Maria", "Dra. Teste Lima", LocalDate(seed.StartAt), LocalTime(seed.StartAt)], button: token);
        var stored = await RescheduleTokenAsync(seed.ReservationId);
        Assert.Equal(SHA256.HashData(WebEncoders.Base64UrlDecode(token)), stored.TokenHash);
        Assert.NotEqual(originalHash, stored.TokenHash);
        Assert.Equal(factory.UtcNow.AddHours(48), stored.ExpiresAt);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(await AuditsAsync()));
    }

    // ---- no duplicates, no manual retry ------------------------------------------------------------------------

    [Fact]
    public async Task A_repeated_request_never_sends_twice_and_says_so()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var first = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED"));
        var second = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("TEMPLATE_TEST_ALREADY_REQUESTED", (await second.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Single(graph.Requests);
        Assert.Single(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task A_double_click_with_two_simultaneous_requests_sends_exactly_once()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph { Delay = TimeSpan.FromMilliseconds(300) };
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var responses = await Task.WhenAll(admin.PostAsync(Body("APPOINTMENT_CONFIRMED")), admin.PostAsync(Body("APPOINTMENT_CONFIRMED")));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(x => x.StatusCode).Order());
        Assert.Single(graph.Requests);
        Assert.Single(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task A_confirmed_meta_error_follows_the_normal_retry_state_and_cannot_be_resent_by_hand()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph();
        graph.Script.Enqueue((HttpStatusCode.InternalServerError, """{"error":{"code":131000,"fbtrace_id":"T"}}"""));
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("APPOINTMENT_CONFIRMED");
        factory.AdvanceTime(TimeSpan.FromMinutes(1));
        var again = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED"));

        Assert.Equal("PENDING", result.Status);                               // normal retry state, retried only by the worker
        Assert.Equal(WhatsAppFailureCodes.ProviderUnavailable, result.Code);
        Assert.Equal(factory.UtcNow.AddSeconds(-30), Assert.Single(await factory.NotificationsAsync()).NextAttemptAt);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task A_timeout_is_unconfirmed_and_never_retried()
    {
        await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var graph = new RecordingGraph { Delay = TimeSpan.FromSeconds(3) };
        using var host = CreateHost(factory, graph, ("Whatsapp:TimeoutSeconds", "1"));
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("APPOINTMENT_CONFIRMED");
        var again = await admin.PostAsync(Body("APPOINTMENT_CONFIRMED"));

        Assert.Equal("UNCONFIRMED", result.Status);
        Assert.Equal(WhatsAppFailureCodes.Timeout, result.Code);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single(graph.Requests);
    }

    // ---- the worker stays off and the rest of the queue is untouched -------------------------------------------

    [Fact]
    public async Task Only_the_test_notice_is_processed_the_queue_and_the_delay_scan_are_untouched_and_the_worker_stays_disabled()
    {
        var seed = await ArrangeAsync(WhatsAppOptInStatus.Granted);
        var queued = await QueueUnrelatedBusinessNoticeAsync();
        await AddLateAppointmentWithWaitingClientAsync(seed);                  // the delay scan would queue a notice for it
        var graph = new RecordingGraph();
        using var host = CreateHost(factory, graph);
        using var admin = await AdminAsync(host);

        var result = await admin.RunAsync("APPOINTMENT_CONFIRMED");

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Single(graph.Requests);
        var notices = await factory.NotificationsAsync();
        Assert.Equal(2, notices.Count);                                       // no DELAY notice was queued
        var untouched = Assert.Single(notices, x => x.Id == queued);
        Assert.Equal(WhatsAppNotificationStatus.Pending, untouched.Status);
        Assert.Equal(0, untouched.Attempts);
        Assert.False(host.Services.GetRequiredService<IOptionsMonitor<WhatsAppNotificationOptions>>().CurrentValue.Enabled);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private sealed record Seed(Guid ReservationId, Guid ProfessionalId, Guid RoomId, Guid CustomerId, DateTimeOffset StartAt);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record ResultPayload(Guid NotificationId, string NotificationType, string Status, string? Code, string? MessageId);
    private sealed record CsrfPayload(string Token);

    private static object Body(string type, string phone = CustomerPhone) => new { notificationType = type, phone };

    /// <summary>A browser-like session on the test host: cookies, CSRF header, login.</summary>
    private sealed class Session(WebApplicationFactory<recepcaototem.Pages.IndexModel> host) : IDisposable
    {
        private readonly HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

        public async Task LoginAsync(GestaoPredio.Infrastructure.Identity.ApplicationUser user)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = user.Email, password = Password })
            };
            request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        }

        public async Task<HttpResponseMessage> PostAsync(object body, string? csrf = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", csrf ?? await CsrfAsync());
            return await client.SendAsync(request);
        }

        public async Task<ResultPayload> RunAsync(string type, string phone = CustomerPhone)
        {
            var response = await PostAsync(Body(type, phone));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ResultPayload>())!;
        }

        private async Task<string> CsrfAsync() =>
            (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<CsrfPayload>())!.Token;

        public void Dispose() => client.Dispose();
    }

    private async Task<GestaoPredio.Infrastructure.Identity.ApplicationUser> UserAsync(string role) =>
        await factory.CreateUserAsync($"template-test-{Guid.NewGuid():N}@lumis.test", Password, [role]);

    private async Task<Session> AdminAsync(WebApplicationFactory<recepcaototem.Pages.IndexModel> host)
    {
        var session = new Session(host);
        await session.LoginAsync(await UserAsync(SystemRoles.Administrador));
        return session;
    }

    private async Task<Seed> ArrangeAsync(WhatsAppOptInStatus optIn, TimeSpan? startIn = null, bool withWaitingVisit = false)
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var professional = Professional.Create("Dra. Teste Lima", "Psicologia", ProfessionalPhone, now);
        var customer = Customer.Create("Maria Clara Souza", CustomerPhone, now);
        if (optIn == WhatsAppOptInStatus.Granted)
        {
            professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now);
            customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);
        }
        if (optIn == WhatsAppOptInStatus.Revoked) customer.RevokeWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, now);
        var room = Room.Create($"Sala Teste {Guid.NewGuid():N}"[..30], null, 10, 50, now);
        var start = now + (startIn ?? TimeSpan.FromHours(3));
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1), "seed", now.AddDays(-1), customer.Id);
        db.AddRange(professional, customer, room, reservation);
        if (withWaitingVisit)
            db.Visits.Add(Visit.Arrive(professional.Id, room.Id, reservation.Id, "Maria Clara Souza", "TOTEM", now, customer.Id));
        await db.SaveChangesAsync();
        return new Seed(reservation.Id, professional.Id, room.Id, customer.Id, reservation.StartAt);
    }

    private async Task<byte[]> CancelForIncidentAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId);
        reservation.Cancel("PROFESSIONAL_INCIDENT", factory.UtcNow, ReservationCancellationReason.ProfessionalUnavailable);
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        db.RescheduleTokens.Add(RescheduleToken.Create(reservation.Id, hash, factory.UtcNow, factory.UtcNow.AddHours(48)));
        await db.SaveChangesAsync();
        return hash;
    }

    /// <summary>A real business notice already waiting in the queue for another customer (worker is off).</summary>
    private async Task<Guid> QueueUnrelatedBusinessNoticeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var professional = await db.Professionals.SingleAsync(x => x.WhatsApp == ProfessionalPhone);
        var other = Customer.Create("Outra Cliente", "+5569981240003", now);
        other.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);
        var room = Room.Create($"Sala Outra {Guid.NewGuid():N}"[..30], null, 10, 50, now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, now.AddDays(1), now.AddDays(1).AddHours(1), "seed", now, other.Id);
        var notice = WhatsAppNotification.AppointmentConfirmed(reservation, now)!;
        db.AddRange(other, room, reservation, notice);
        await db.SaveChangesAsync();
        return notice.Id;
    }

    private async Task AddLateAppointmentWithWaitingClientAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var late = Reservation.CreateApproved(seed.RoomId, seed.ProfessionalId, now.AddMinutes(-20), now.AddMinutes(40), "seed", now.AddDays(-1),
            (await db.Customers.SingleAsync(x => x.NormalizedPhone == "+5569981240003")).Id);
        db.Reservations.Add(late);
        db.Visits.Add(Visit.Arrive(seed.ProfessionalId, seed.RoomId, late.Id, "Outra Cliente", "TOTEM", now.AddMinutes(-25), late.CustomerId));
        await db.SaveChangesAsync();
    }

    private async Task<List<GestaoPredio.Domain.Auditing.AuditEntry>> AuditsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.AsNoTracking()
            .Where(x => x.Action.StartsWith(WhatsappNotificationEndpoints.TemplateTestAuditAction)).ToListAsync();
    }

    private async Task<RescheduleToken> RescheduleTokenAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RescheduleTokens.AsNoTracking()
            .SingleAsync(x => x.ReservationId == reservationId);
    }

    private static readonly TimeZoneInfo PortoVelho = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
    private static string LocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("HH:mm");
    private static string LocalDate(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("dd/MM/yyyy");
}
