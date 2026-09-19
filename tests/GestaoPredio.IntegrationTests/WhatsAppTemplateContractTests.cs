using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Whatsapp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using recepcaototem.Features.Whatsapp;
using static GestaoPredio.IntegrationTests.WhatsAppContractSupport;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Contract of the six approved WhatsApp templates: the real WhatsAppCloudApiService builds the request, and only the
/// network is replaced (the handler records the request and answers like the Cloud API). The dispatcher is run by
/// hand; the background worker stays disabled. Nothing reaches Meta.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class WhatsAppTemplateContractTests(ModulesApiFactory factory)
{
    private const string ProfessionalPhone = "+5569981230001";
    private const string CustomerPhone = "+5569981230002";

    private WebApplicationFactory<recepcaototem.Pages.IndexModel> CreateHost(RecordingGraph graph, params (string Key, string Value)[] extra) =>
        WhatsAppContractSupport.CreateHost(factory, graph, extra);

    // ---- the six contracts --------------------------------------------------------------------------------------

    [Fact]
    public async Task Professional_client_checked_in_goes_to_the_professional_with_three_body_parameters()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromMinutes(5));
        var visitId = await AddVisitAsync(seed, notice: v => WhatsAppNotification.ClientCheckedIn(v, factory.UtcNow));
        var graph = new RecordingGraph();
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);

        var request = Assert.Single(graph.Requests);
        var notice = await NoticeAsync();
        AssertEnvelope(request, ProfessionalPhone, notice);
        AssertTemplate(request, "professional_client_checked_in",
            ["Dra. Contrato Silva", "Maria", LocalTime(seed.StartAt)], button: null);
        Assert.NotEqual(Guid.Empty, visitId);
    }

    [Fact]
    public async Task Client_professional_delayed_goes_to_the_customer_with_the_minutes_late()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromMinutes(-12));
        await AddVisitAsync(seed, notice: null);
        await AddReservationNoticeAsync(seed, (r, now) => WhatsAppNotification.ProfessionalDelayed(r, 0, now));
        var graph = new RecordingGraph();
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);

        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, CustomerPhone, await NoticeAsync());
        AssertTemplate(request, "client_professional_delayed", ["Maria", "Dra. Contrato Silva", "12"], button: null);
    }

    [Fact]
    public async Task Client_professional_cancelled_carries_four_body_parameters_and_the_reschedule_token_as_the_url_button()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        var originalHash = await CancelForIncidentAsync(seed);
        factory.AdvanceTime(TimeSpan.FromMinutes(7));                        // the dispatcher runs later
        var graph = new RecordingGraph();
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);

        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, CustomerPhone, await NoticeAsync());
        var token = ButtonParameter(request);
        AssertTemplate(request, "client_professional_cancelled",
            ["Maria", "Dra. Contrato Silva", LocalDate(seed.StartAt), LocalTime(seed.StartAt)], button: token);

        // Created at send time, only its hash is stored, valid for 48 h from the send, and it resolves.
        var stored = await RescheduleTokenAsync(seed.ReservationId);
        Assert.Equal(SHA256.HashData(WebEncoders.Base64UrlDecode(token)), stored.TokenHash);
        Assert.NotEqual(originalHash, stored.TokenHash);
        Assert.Equal(factory.UtcNow.AddHours(48), stored.ExpiresAt);
        Assert.Equal(43, token.Length);                                       // 32 random bytes, base64url
        Assert.False(await AnyTextColumnContainsAsync("WhatsAppNotifications", token));
        Assert.False(await AnyTextColumnContainsAsync("WhatsAppMessages", token));
        Assert.False(await AnyTextColumnContainsAsync("AuditEntries", token));
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token })).StatusCode);
    }

    [Fact]
    public async Task Appointment_cancelled_has_the_same_four_body_parameters_and_no_button()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        await MutateReservationAsync(seed, r => r.Cancel("admin", factory.UtcNow));
        await AddReservationNoticeAsync(seed, WhatsAppNotification.ReservationCancelled);
        var graph = new RecordingGraph();
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);

        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, CustomerPhone, await NoticeAsync());
        AssertTemplate(request, "appointment_cancelled",
            ["Maria", "Dra. Contrato Silva", LocalDate(seed.StartAt), LocalTime(seed.StartAt)], button: null);
    }

    [Fact]
    public async Task Appointment_rescheduled_sends_the_new_date_and_time_of_the_replacement()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        var newStart = seed.StartAt.AddDays(2).AddHours(1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId);
            var replacement = Reservation.CreateApprovedReschedule(original, newStart, newStart.AddHours(1), "admin", factory.UtcNow);
            original.Cancel("admin", factory.UtcNow);
            db.Reservations.Add(replacement);
            db.WhatsAppNotifications.Add(WhatsAppNotification.AppointmentRescheduled(replacement, factory.UtcNow)!);
            await db.SaveChangesAsync();
        }
        var graph = new RecordingGraph();
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);

        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, CustomerPhone, await NoticeAsync());
        AssertTemplate(request, "appointment_rescheduled",
            ["Maria", "Dra. Contrato Silva", LocalDate(newStart), LocalTime(newStart)], button: null);
    }

    [Fact]
    public async Task Appointment_confirmed_sends_the_date_and_time_of_the_booking()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        await AddReservationNoticeAsync(seed, WhatsAppNotification.AppointmentConfirmed);
        var graph = new RecordingGraph();
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);

        var request = Assert.Single(graph.Requests);
        AssertEnvelope(request, CustomerPhone, await NoticeAsync());
        AssertTemplate(request, "appointment_confirmed",
            ["Maria", "Dra. Contrato Silva", LocalDate(seed.StartAt), LocalTime(seed.StartAt)], button: null);
    }

    // ---- one controlled notice through the whole pipeline -------------------------------------------------------

    [Fact]
    public async Task One_controlled_notice_goes_pending_sending_accepted_sent_delivered_read_and_a_replayed_webhook_changes_nothing()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        await AddReservationNoticeAsync(seed, WhatsAppNotification.AppointmentConfirmed);
        var pending = await NoticeAsync();
        Assert.Equal(WhatsAppNotificationStatus.Pending, pending.Status);
        Assert.Equal(0, pending.Attempts);
        Assert.Equal($"CONFIRM:{seed.ReservationId}", pending.IdempotencyKey);

        var graph = new RecordingGraph();
        WhatsAppNotificationStatus? duringCall = null;
        graph.OnRequest = async () => duringCall = (await NoticeAsync()).Status;
        using var host = CreateHost(graph);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await ModulesApiFactory.DispatchAsync(host);

        // PENDING → (PROCESSING, claimed) → SENDING committed before the call → ACCEPTED with the wamid.
        Assert.Equal(WhatsAppNotificationStatus.Sending, duringCall);
        var accepted = await NoticeAsync();
        Assert.Equal(WhatsAppNotificationStatus.Accepted, accepted.Status);
        Assert.Equal("wamid.CONTRACT.1", accepted.MessageId);
        Assert.Equal(1, accepted.Attempts);
        var message = Assert.Single(await MessagesAsync());
        Assert.Equal("wamid.CONTRACT.1", message.MessageId);
        Assert.Equal(WhatsAppMessageType.Template, message.MessageType);
        Assert.Equal(WhatsAppDeliveryStatus.Accepted, message.Status);

        foreach (var (raw, expected) in new[]
                 {
                     ("sent", WhatsAppNotificationStatus.Sent),
                     ("delivered", WhatsAppNotificationStatus.Delivered),
                     ("read", WhatsAppNotificationStatus.Read)
                 })
        {
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, "wamid.CONTRACT.1", raw, accepted.CallbackData)).StatusCode);
            Assert.Equal(expected, (await NoticeAsync()).Status);
        }

        // Meta redelivers: same statuses again, out of order too. Nothing moves back, nothing new is queued or sent.
        foreach (var raw in new[] { "delivered", "sent", "read" })
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, "wamid.CONTRACT.1", raw, accepted.CallbackData)).StatusCode);
        factory.AdvanceTime(TimeSpan.FromHours(1));
        await ModulesApiFactory.DispatchAsync(host);

        var final = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Read, final.Status);
        Assert.Equal(1, final.Attempts);
        Assert.Single(graph.Requests);
        Assert.Equal(WhatsAppDeliveryStatus.Read, Assert.Single(await MessagesAsync()).Status);
    }

    // ---- reschedule link under failures, with the real client -------------------------------------------------

    [Fact]
    public async Task A_confirmed_meta_error_is_retried_with_a_new_link_and_only_the_delivered_link_works()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        await CancelForIncidentAsync(seed);
        var graph = new RecordingGraph();
        graph.Script.Enqueue((HttpStatusCode.InternalServerError, """{"error":{"code":131000,"fbtrace_id":"T"}}"""));
        using var host = CreateHost(graph);

        await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(WhatsAppNotificationStatus.Pending, (await NoticeAsync()).Status);
        factory.AdvanceTime(TimeSpan.FromSeconds(30));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(2, graph.Requests.Count);
        var refused = ButtonParameter(graph.Requests[0]);
        var delivered = ButtonParameter(graph.Requests[1]);
        Assert.NotEqual(refused, delivered);
        Assert.Equal(WhatsAppNotificationStatus.Accepted, (await NoticeAsync()).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = refused })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = delivered })).StatusCode);
    }

    [Fact]
    public async Task A_timeout_leaves_the_notice_unconfirmed_without_retry_and_keeps_the_link_it_carried()
    {
        var seed = await ArrangeAsync(startIn: TimeSpan.FromHours(3));
        await CancelForIncidentAsync(seed);
        var graph = new RecordingGraph { Delay = TimeSpan.FromSeconds(3) };
        using var host = CreateHost(graph, ("Whatsapp:TimeoutSeconds", "1"));

        await ModulesApiFactory.DispatchAsync(host);
        var link = ButtonParameter(Assert.Single(graph.Requests));
        var unconfirmed = await NoticeAsync();
        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, unconfirmed.Status);
        Assert.Equal(WhatsAppFailureCodes.Timeout, unconfirmed.LastErrorCode);

        factory.AdvanceTime(TimeSpan.FromMinutes(5));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(graph.Requests);                                        // no second message
        Assert.Equal(SHA256.HashData(WebEncoders.Base64UrlDecode(link)), (await RescheduleTokenAsync(seed.ReservationId)).TokenHash);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = link })).StatusCode);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private sealed record Seed(Guid ReservationId, Guid ProfessionalId, Guid RoomId, Guid CustomerId, DateTimeOffset StartAt);


    private async Task<Seed> ArrangeAsync(TimeSpan startIn)
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var professional = Professional.Create("Dra. Contrato Silva", "Psicologia", ProfessionalPhone, now);
        professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now);
        var customer = Customer.Create("Maria Clara Souza", CustomerPhone, now);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);
        var room = Room.Create($"Sala Contrato {Guid.NewGuid():N}"[..30], null, 10, 50, now);
        var start = now + startIn;
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1), "seed", now.AddDays(-1), customer.Id);
        db.AddRange(professional, customer, room, reservation);
        await db.SaveChangesAsync();
        return new Seed(reservation.Id, professional.Id, room.Id, customer.Id, reservation.StartAt);
    }

    private async Task<Guid> AddVisitAsync(Seed seed, Func<Visit, WhatsAppNotification>? notice)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = Visit.Arrive(seed.ProfessionalId, seed.RoomId, seed.ReservationId, "Maria Clara Souza", "TOTEM", factory.UtcNow, seed.CustomerId);
        db.Visits.Add(visit);
        if (notice is not null) db.WhatsAppNotifications.Add(notice(visit));
        await db.SaveChangesAsync();
        return visit.Id;
    }

    private async Task AddReservationNoticeAsync(Seed seed, Func<Reservation, DateTimeOffset, WhatsAppNotification?> create)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == seed.ReservationId);
        db.WhatsAppNotifications.Add(create(reservation, factory.UtcNow)!);
        await db.SaveChangesAsync();
    }

    private async Task MutateReservationAsync(Seed seed, Action<Reservation> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        change(await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId));
        await db.SaveChangesAsync();
    }

    /// <summary>What the incident endpoint commits: cancellation, a token row (hash of a value that never leaves), the notice.</summary>
    private async Task<byte[]> CancelForIncidentAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId);
        reservation.Cancel("PROFESSIONAL_INCIDENT", factory.UtcNow, ReservationCancellationReason.ProfessionalUnavailable);
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        db.RescheduleTokens.Add(RescheduleToken.Create(reservation.Id, hash, factory.UtcNow, factory.UtcNow.AddHours(48)));
        db.WhatsAppNotifications.Add(WhatsAppNotification.ReservationCancelled(reservation, factory.UtcNow)!);
        await db.SaveChangesAsync();
        return hash;
    }

    private async Task<WhatsAppNotification> NoticeAsync() => Assert.Single(await factory.NotificationsAsync());

    private async Task<List<WhatsAppMessage>> MessagesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().WhatsAppMessages.AsNoTracking().ToListAsync();
    }

    private async Task<RescheduleToken> RescheduleTokenAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RescheduleTokens.AsNoTracking()
            .SingleAsync(x => x.ReservationId == reservationId);
    }

    /// <summary>Scans every text column of a table for the value, so a raw token can never hide in it.</summary>
    private async Task<bool> AnyTextColumnContainsAsync(string table, string value)
    {
        // Fixed SQL per table: an identifier cannot be a parameter, so no string is ever interpolated into the query.
        var sql = table switch
        {
            "WhatsAppNotifications" => "SELECT CAST(t AS text) AS \"Value\" FROM \"WhatsAppNotifications\" AS t",
            "WhatsAppMessages" => "SELECT CAST(t AS text) AS \"Value\" FROM \"WhatsAppMessages\" AS t",
            "AuditEntries" => "SELECT CAST(t AS text) AS \"Value\" FROM \"AuditEntries\" AS t",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database
            .SqlQueryRaw<string>(sql).ToListAsync();
        return rows.Any(row => row.Contains(value, StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string messageId, string status, string callbackData)
    {
        var body = $$$"""
            {"object":"whatsapp_business_account","entry":[{"id":"1500039464855591","changes":[{"field":"messages","value":{
              "messaging_product":"whatsapp","metadata":{"phone_number_id":"{{{PhoneNumberId}}}"},
              "statuses":[{"id":"{{{messageId}}}","status":"{{{status}}}","timestamp":"1789670000","recipient_id":"{{{CustomerPhone.TrimStart('+')}}}",
                "biz_opaque_callback_data":"{{{callbackData}}}"}]}}]}]}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, WhatsappEndpoints.WebhookPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader,
            "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(body))));
        return await client.SendAsync(request);
    }

    private static readonly TimeZoneInfo PortoVelho = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
    private static string LocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("HH:mm");
    private static string LocalDate(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("dd/MM/yyyy");
}
