using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalReservationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Linked_professional_requests_and_reads_only_owned_reservations()
    {
        await factory.ResetAsync();
        var seeded = await SeedLinkedProfessionalsAsync();
        var start = factory.UtcNow.AddHours(2);
        await LoginAsync(seeded.OwnerEmail);

        var response = await factory.PostWithCsrfAsync("/api/professional/reservations",
            new { roomId = seeded.OwnerRoomId, startAt = start, endAt = start.AddHours(1) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal(seeded.OwnerProfessionalId, created.ProfessionalId);
        Assert.Equal("PENDING", created.Status);
        var listResponse = await factory.Client.GetAsync("/api/professional/reservations");
        listResponse.EnsureSuccessStatusCode();
        var page = (await listResponse.Content.ReadFromJsonAsync<ReservationPage>())!;
        Assert.Single(page.Items);
        Assert.Equal(created.Id, page.Items[0].Id);
        Assert.Equal(HttpStatusCode.OK,
            (await factory.Client.GetAsync($"/api/professional/reservations/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/professional/reservations/{seeded.ForeignReservationId}")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.ReservationRequested && entry.TargetEntityId == created.Id));
    }

    [Fact]
    public async Task Request_rejects_professional_id_overposting_and_insufficient_notice()
    {
        await factory.ResetAsync();
        var seeded = await SeedLinkedProfessionalsAsync();
        await LoginAsync(seeded.OwnerEmail);
        var start = factory.UtcNow.AddHours(2);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync("/api/professional/reservations", new
            {
                roomId = seeded.OwnerRoomId,
                professionalId = seeded.ForeignProfessionalId,
                startAt = start,
                endAt = start.AddHours(1)
            })).StatusCode);

        var tooSoon = await factory.PostWithCsrfAsync("/api/professional/reservations", new
        {
            roomId = seeded.OwnerRoomId,
            startAt = factory.UtcNow.AddMinutes(30),
            endAt = factory.UtcNow.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooSoon.StatusCode);
        Assert.Equal("INVALID_RESERVATION", (await tooSoon.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Unlinked_professional_gets_empty_list_and_cannot_create()
    {
        await factory.ResetAsync();
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, factory.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
        }
        var user = await factory.CreateUserAsync($"unlinked-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        await LoginAsync(user.Email!);
        var page = (await (await factory.Client.GetAsync("/api/professional/reservations"))
            .Content.ReadFromJsonAsync<ReservationPage>())!;
        Assert.Empty(page.Items);
        var start = factory.UtcNow.AddHours(2);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.PostWithCsrfAsync("/api/professional/reservations",
                new { roomId = room.Id, startAt = start, endAt = start.AddHours(1) })).StatusCode);
    }

    [Fact]
    public async Task Owned_list_filters_by_status_in_the_database_and_rejects_invalid_status()
    {
        await factory.ResetAsync();
        var seeded = await SeedLinkedProfessionalsAsync();
        var now = factory.UtcNow;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Reservations.Add(Reservation.CreateApproved(
                seeded.OwnerRoomId, seeded.OwnerProfessionalId, now.AddDays(3), now.AddDays(3).AddHours(1),
                "admin", now));
            await db.SaveChangesAsync();
        }
        await LoginAsync(seeded.OwnerEmail);
        var start = now.AddHours(2);
        (await factory.PostWithCsrfAsync("/api/professional/reservations",
            new { roomId = seeded.OwnerRoomId, startAt = start, endAt = start.AddHours(1) })).EnsureSuccessStatusCode();

        var pending = (await (await factory.Client.GetAsync("/api/professional/reservations?status=PENDING"))
            .Content.ReadFromJsonAsync<ReservationPage>())!;
        Assert.Single(pending.Items);
        Assert.Equal("PENDING", pending.Items[0].Status);
        var invalid = await factory.Client.GetAsync("/api/professional/reservations?status=UNKNOWN");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("INVALID_STATUS", (await invalid.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task New_request_revalidates_the_current_identity_link_after_acquiring_the_resource_lock()
    {
        await factory.ResetAsync();
        var seeded = await SeedLinkedProfessionalsAsync();
        var blockingLock = new BlockingResourceLock();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILeaseResourceLock>();
            services.AddSingleton<ILeaseResourceLock>(blockingLock);
        }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await LoginAsync(client, seeded.OwnerEmail);
        var start = factory.UtcNow.AddHours(2);
        var requestTask = PostWithCsrfAsync(client, "/api/professional/reservations",
            new { roomId = seeded.OwnerRoomId, startAt = start, endAt = start.AddHours(1) });

        await blockingLock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var professional = await db.Professionals.SingleAsync(value => value.Id == seeded.OwnerProfessionalId);
            professional.UnlinkUser(factory.UtcNow);
            await db.SaveChangesAsync();
        }
        blockingLock.Release.TrySetResult();

        var response = await requestTask;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await verificationDb.Reservations.AnyAsync(value =>
            value.ProfessionalId == seeded.OwnerProfessionalId));
        Assert.False(await verificationDb.AuditEntries.AnyAsync(value =>
            value.Action == AuditActions.ReservationRequested));
    }

    [Fact]
    public async Task Reservation_response_includes_customer_name_when_a_customer_is_linked()
    {
        await factory.ResetAsync();
        var seeded = await SeedLinkedProfessionalsAsync();
        var now = factory.UtcNow;
        var customer = Customer.Create("Ana Beatriz", "+5565977777777", now);
        var reservation = Reservation.CreateApproved(seeded.OwnerRoomId, seeded.OwnerProfessionalId,
            now.AddDays(1), now.AddDays(1).AddHours(1), "admin", now, customer.Id);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(customer, reservation);
            await db.SaveChangesAsync();
        }
        await LoginAsync(seeded.OwnerEmail);

        var detailResponse = await factory.Client.GetAsync($"/api/professional/reservations/{reservation.Id}");
        detailResponse.EnsureSuccessStatusCode();
        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ana Beatriz", detailBody.GetProperty("customerName").GetString());

        var listResponse = await factory.Client.GetAsync("/api/professional/reservations");
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = listBody.GetProperty("items").EnumerateArray()
            .Single(element => element.GetProperty("id").GetGuid() == reservation.Id);
        Assert.Equal("Ana Beatriz", item.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task Reservation_response_has_null_customer_name_when_no_customer_is_linked()
    {
        await factory.ResetAsync();
        var seeded = await SeedLinkedProfessionalsAsync();
        var now = factory.UtcNow;
        var reservation = Reservation.CreateApproved(seeded.OwnerRoomId, seeded.OwnerProfessionalId,
            now.AddDays(1), now.AddDays(1).AddHours(1), "admin", now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
        }
        await LoginAsync(seeded.OwnerEmail);

        var response = await factory.Client.GetAsync($"/api/professional/reservations/{reservation.Id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("customerName").ValueKind is JsonValueKind.Null);
    }

    private async Task<SeededResources> SeedLinkedProfessionalsAsync()
    {
        await factory.SeedDefaultOperatingHoursAsync();
        var owner = await factory.CreateUserAsync($"owner-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var other = await factory.CreateUserAsync($"other-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var now = factory.UtcNow;
        var ownerProfessional = Professional.Create("Profissional dono", "Fisioterapia", "+5565999999999", now);
        ownerProfessional.LinkUser(owner.Id, now);
        var otherProfessional = Professional.Create("Outro profissional", "Psicologia", "+5565988888888", now);
        otherProfessional.LinkUser(other.Id, now);
        var ownerRoom = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, now);
        var otherRoom = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, now);
        var foreign = Reservation.CreateApproved(otherRoom.Id, otherProfessional.Id,
            now.AddDays(2), now.AddDays(2).AddHours(1), "admin", now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(ownerProfessional, otherProfessional, ownerRoom, otherRoom, foreign);
        await db.SaveChangesAsync();
        return new SeededResources(owner.Email!, ownerProfessional.Id, otherProfessional.Id,
            ownerRoom.Id, foreign.Id);
    }

    private async Task LoginAsync(string email) =>
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var token = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = Password })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<CsrfPayload>())!.Token;

    private sealed record SeededResources(string OwnerEmail, Guid OwnerProfessionalId,
        Guid ForeignProfessionalId, Guid OwnerRoomId, Guid ForeignReservationId);
    private sealed record ReservationPage(IReadOnlyList<ReservationPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record ReservationPayload(Guid Id, Guid ProfessionalId, string Status);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record CsrfPayload(string Token);

    private sealed class BlockingResourceLock : ILeaseResourceLock
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AcquireAsync(LeaseResourceLockRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
