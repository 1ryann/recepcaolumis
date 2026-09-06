using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        var start = DateTimeOffset.UtcNow.AddHours(2);
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
        var start = DateTimeOffset.UtcNow.AddHours(2);
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
            startAt = DateTimeOffset.UtcNow.AddMinutes(30),
            endAt = DateTimeOffset.UtcNow.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooSoon.StatusCode);
        Assert.Equal("INVALID_RESERVATION", (await tooSoon.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Unlinked_professional_gets_empty_list_and_cannot_create()
    {
        await factory.ResetAsync();
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, DateTimeOffset.UtcNow);
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
        var start = DateTimeOffset.UtcNow.AddHours(2);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.PostWithCsrfAsync("/api/professional/reservations",
                new { roomId = room.Id, startAt = start, endAt = start.AddHours(1) })).StatusCode);
    }

    private async Task<SeededResources> SeedLinkedProfessionalsAsync()
    {
        var owner = await factory.CreateUserAsync($"owner-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var other = await factory.CreateUserAsync($"other-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var now = DateTimeOffset.UtcNow;
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

    private sealed record SeededResources(string OwnerEmail, Guid OwnerProfessionalId,
        Guid ForeignProfessionalId, Guid OwnerRoomId, Guid ForeignReservationId);
    private sealed record ReservationPage(IReadOnlyList<ReservationPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record ReservationPayload(Guid Id, Guid ProfessionalId, string Status);
    private sealed record ErrorPayload(string Code, string Message);
}
