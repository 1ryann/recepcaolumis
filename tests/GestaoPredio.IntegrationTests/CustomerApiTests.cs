using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class CustomerApiTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Remote_registration_creates_customer_account_and_me_is_scoped_to_principal()
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/register")
        {
            Content = JsonContent.Create(new { name = "  Cliente   Teste ", phone = "(69) 99999-9999", email = $"customer-{Guid.NewGuid():N}@lumis.test", password = "Valid-Password-123!", confirmation = "Valid-Password-123!" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await factory.Client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, responseBody);
        var me = await factory.Client.GetAsync("/api/customer/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("senha1", HttpStatusCode.Created)]      // six characters, letter + digit: the operator's policy
    [InlineData("abc12", HttpStatusCode.BadRequest)]    // five characters
    [InlineData("senhasenha", HttpStatusCode.BadRequest)] // no digit
    public async Task Registration_accepts_a_six_character_password_without_symbols_or_capitals(string password, HttpStatusCode expected)
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/register")
        {
            Content = JsonContent.Create(new { name = "Cliente Teste", phone = "(69) 99999-9999",
                email = $"password-{Guid.NewGuid():N}@lumis.test", password, confirmation = password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await factory.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_phone_already_registered_is_refused_with_the_same_generic_error()
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        async Task<HttpResponseMessage> RegisterAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/register")
            {
                Content = JsonContent.Create(new { name = "Cliente Teste", phone = "(69) 99999-9999",
                    email = $"dup-{Guid.NewGuid():N}@lumis.test", password = "senha1", confirmation = "senha1" })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            return await factory.Client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync()).StatusCode);
        // Same number, a brand-new e-mail: refused, because one account per number.
        var second = await RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("INVALID_CUSTOMER_REGISTRATION", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Totem_lookup_masks_existing_customer_without_exposing_identity()
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/customer/register")
        {
            Content = JsonContent.Create(new { name = "Carlos Oliveira", phone = "(69) 99999-9999", email = $"lookup-{Guid.NewGuid():N}@lumis.test", password = "Valid-Password-123!", confirmation = "Valid-Password-123!" })
        };
        register.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.Created, (await factory.Client.SendAsync(register)).StatusCode);
        var lookup = await factory.Client.PostAsJsonAsync("/api/totem/customers/resolve", new { name = "Carlos", phone = "69999999999" });
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var body = await lookup.Content.ReadAsStringAsync();
        Assert.Contains("Carlos O.", body);
        Assert.DoesNotContain("+5569999999999", body);
    }

    [Fact]
    public async Task Customer_cancel_and_reschedule_require_the_current_xmin_token()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerReservationAsync(factory.UtcNow.AddHours(4));
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var detail = (await (await factory.Client.GetAsync($"/api/customer/reservations/{seed.ReservationId}")).Content
            .ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/cancel", new { concurrencyToken = "invalid" })).StatusCode);

        var rescheduledStart = seed.StartAt.AddHours(2);
        var rescheduled = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/reschedule",
            new { professionalId = seed.ProfessionalId, startAt = rescheduledStart, endAt = rescheduledStart.AddHours(1), concurrencyToken = detail.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);
        var replacement = (await rescheduled.Content.ReadFromJsonAsync<ReservationPayload>())!;

        var staleReschedule = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/reschedule",
            new { professionalId = seed.ProfessionalId, startAt = rescheduledStart.AddHours(1), endAt = rescheduledStart.AddHours(2), concurrencyToken = detail.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, staleReschedule.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await staleReschedule.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var stale = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/cancel",
            new { concurrencyToken = detail.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var current = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{replacement.Id}/cancel",
            new { concurrencyToken = replacement.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task Customer_reschedule_revokes_the_old_qr_token()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerReservationAsync(factory.UtcNow.AddMinutes(-10));
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);
        var issued = (await (await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/check-in-token", new { }))
            .Content.ReadFromJsonAsync<TokenPayload>())!;
        var detail = (await (await factory.Client.GetAsync($"/api/customer/reservations/{seed.ReservationId}")).Content
            .ReadFromJsonAsync<ReservationPayload>())!;
        var start = factory.UtcNow.AddHours(2);
        var response = await factory.PostWithCsrfAsync($"/api/customer/reservations/{seed.ReservationId}/reschedule",
            new { professionalId = seed.ProfessionalId, startAt = start, endAt = start.AddHours(1), concurrencyToken = detail.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issued.Token })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var token = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CheckInTokens
            .SingleAsync(x => x.ReservationId == seed.ReservationId);
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public async Task Totem_qr_checkin_creates_one_waiting_visit_and_is_idempotent()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerReservationAsync(factory.UtcNow.AddMinutes(-10));
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);

        var tokenResponse = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/check-in-token", new { });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var issued = (await tokenResponse.Content.ReadFromJsonAsync<TokenPayload>())!;
        Assert.False(string.IsNullOrWhiteSpace(issued.Token));

        var resolve = await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issued.Token });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var preview = await resolve.Content.ReadAsStringAsync();
        Assert.Contains(seed.ProfessionalName, preview);
        Assert.Contains(seed.RoomName, preview);
        Assert.DoesNotContain(seed.CustomerPhone, preview);

        var confirm = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issued.Token });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var first = (await confirm.Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Equal("WAITING", first.Status);

        var replay = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issued.Token });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var second = (await replay.Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Equal(first.VisitId, second.VisitId);
        // The replayed confirm reuses the visit, so exactly one arrival notice was queued for the professional.
        var notice = Assert.Single(await factory.NotificationsAsync(), x => x.Type == WhatsAppNotificationType.ClientCheckedIn);
        Assert.Equal(WhatsAppNotificationRecipient.Professional, notice.Recipient);
        Assert.Equal(seed.ProfessionalId, notice.ProfessionalId);
        Assert.Equal(first.VisitId, notice.VisitId);
        Assert.Equal(seed.ReservationId, notice.ReservationId);
        Assert.Equal(WhatsAppNotificationStatus.Pending, notice.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = await db.Visits.SingleAsync(x => x.Id == first.VisitId);
        var token = await db.CheckInTokens.SingleAsync(x => x.ReservationId == seed.ReservationId);
        Assert.Equal(seed.CustomerId, visit.CustomerId);
        Assert.Equal(seed.ReservationId, visit.ReservationId);
        Assert.Equal(seed.ProfessionalId, visit.ProfessionalId);
        Assert.Equal(seed.RoomId, visit.RoomId);
        Assert.Equal(seed.CustomerName, visit.VisitorName);
        Assert.NotNull(token.UsedAt);

        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/customer/me")).StatusCode);
    }

    [Fact]
    public async Task Administrative_cancellation_revokes_an_issued_qr_token()
    {
        await factory.ResetAsync();
        var seed = await SeedCustomerReservationAsync(factory.UtcNow.AddMinutes(-10));
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);
        var issued = (await (await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{seed.ReservationId}/check-in-token", new { }))
            .Content.ReadFromJsonAsync<TokenPayload>())!;
        var admin = await factory.CreateUserAsync($"admin-{Guid.NewGuid():N}@lumis.test", seed.Password,
            [GestaoPredio.Domain.Security.SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, seed.Password)).StatusCode);
        var cancelled = await factory.PostWithCsrfAsync($"/api/admin/reservations/{seed.ReservationId}/cancel",
            new { concurrencyToken = seed.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issued.Token })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var token = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CheckInTokens
            .SingleAsync(x => x.ReservationId == seed.ReservationId);
        Assert.NotNull(token.RevokedAt);
    }

    private async Task<CustomerSeed> SeedCustomerReservationAsync(DateTimeOffset startAt)
    {
        await factory.SeedDefaultOperatingHoursAsync();
        var password = "Valid-Password-123!";
        var user = await factory.CreateUserAsync($"customer-{Guid.NewGuid():N}@lumis.test", password,
            [GestaoPredio.Domain.Security.SystemRoles.Customer], displayName: "Carlos Oliveira");
        var now = factory.UtcNow;
        var room = Room.Create($"Sala QR {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional QR", "Fisioterapia", $"659{Random.Shared.Next(10000000, 99999999)}", now);
        var customer = Customer.Create("Carlos Oliveira", "+5569999999999", now);
        customer.LinkUser(user.Id, now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, startAt, startAt.AddHours(1), user.Id, now, customer.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, professional, customer, reservation);
        await db.SaveChangesAsync();
        return new CustomerSeed(user.Email!, password, customer.Id, customer.Name, customer.Phone, room.Id, room.Name,
            professional.Id, professional.Name, reservation.Id, reservation.StartAt,
            recepcaototem.Features.Common.ConcurrencyToken.Encode(reservation.Version));
    }

    private sealed record CustomerSeed(string Email, string Password, Guid CustomerId, string CustomerName,
        string CustomerPhone, Guid RoomId, string RoomName, Guid ProfessionalId, string ProfessionalName,
        Guid ReservationId, DateTimeOffset StartAt, string ConcurrencyToken);
    private sealed record ReservationPayload(Guid Id, string ConcurrencyToken);
    private sealed record TokenPayload(string Token);
    private sealed record VisitPayload(Guid VisitId, string Status);
    private sealed record ErrorPayload(string Code, string Message);
}
