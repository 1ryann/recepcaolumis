using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ReservationWorkflowTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Administrator_approves_pending_request_and_stale_token_is_rejected()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(pending: true);
        await LoginAsync(seed.Admin);

        var approvedResponse = await factory.PostWithCsrfAsync(
            $"/api/admin/reservations/{seed.ReservationId}/approve",
            new { concurrencyToken = seed.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        var approved = (await approvedResponse.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal("APPROVED", approved.Status);
        Assert.NotEqual(seed.ConcurrencyToken, approved.ConcurrencyToken);
        var stale = await factory.PostWithCsrfAsync(
            $"/api/admin/reservations/{seed.ReservationId}/cancel",
            new { concurrencyToken = seed.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.ReservationApproved && entry.TargetEntityId == seed.ReservationId));
        Assert.Equal(0, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.ReservationCancelled && entry.TargetEntityId == seed.ReservationId));
    }

    [Fact]
    public async Task Rejection_requires_reason_and_preserves_it_on_the_request()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(pending: true);
        await LoginAsync(seed.Manager);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync($"/api/admin/reservations/{seed.ReservationId}/reject",
                new { reason = " ", concurrencyToken = seed.ConcurrencyToken })).StatusCode);

        var response = await factory.PostWithCsrfAsync($"/api/admin/reservations/{seed.ReservationId}/reject",
            new { reason = "Horário indisponível", concurrencyToken = seed.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = (await response.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal("Horário indisponível", rejected.RejectionReason);
    }

    [Fact]
    public async Task Professional_reschedule_preserves_original_until_approval_then_replaces_it()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(pending: false);
        await LoginAsync(seed.Owner);
        var newStart = seed.StartAt.AddHours(3);

        var requestResponse = await factory.PostWithCsrfAsync(
            $"/api/professional/reservations/{seed.ReservationId}/reschedule-request",
            new { startAt = newStart, endAt = newStart.AddHours(1), concurrencyToken = seed.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        var request = (await requestResponse.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal("RESCHEDULE", request.Kind);
        Assert.Equal("PENDING", request.Status);
        Assert.Equal(seed.ReservationId, request.OriginalReservationId);
        await AssertStatusAsync(seed.ReservationId, ReservationStatus.Approved);

        await LoginAsync(seed.Admin);
        var approval = await factory.PostWithCsrfAsync($"/api/admin/reservations/{request.Id}/approve",
            new { concurrencyToken = request.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal("APPROVED", (await approval.Content.ReadFromJsonAsync<ReservationPayload>())!.Status);
        await AssertStatusAsync(seed.ReservationId, ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task Professional_cancellation_request_preserves_original_until_approval()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(pending: false);
        await LoginAsync(seed.Owner);

        var requestResponse = await factory.PostWithCsrfAsync(
            $"/api/professional/reservations/{seed.ReservationId}/cancel-request",
            new { concurrencyToken = seed.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        var request = (await requestResponse.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal("CANCELLATION", request.Kind);
        Assert.Equal("PENDING", request.Status);
        await AssertStatusAsync(seed.ReservationId, ReservationStatus.Approved);

        await LoginAsync(seed.Manager);
        var approval = await factory.PostWithCsrfAsync($"/api/admin/reservations/{request.Id}/approve",
            new { concurrencyToken = request.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        await AssertStatusAsync(seed.ReservationId, ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task Professional_cannot_request_a_change_for_another_professionals_reservation()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(pending: false);
        var other = await factory.CreateUserAsync($"other-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        await LoginAsync(other);
        var newStart = seed.StartAt.AddHours(3);

        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.PostWithCsrfAsync(
                $"/api/professional/reservations/{seed.ReservationId}/reschedule-request",
                new { startAt = newStart, endAt = newStart.AddHours(1), concurrencyToken = seed.ConcurrencyToken }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.PostWithCsrfAsync(
                $"/api/professional/reservations/{seed.ReservationId}/cancel-request",
                new { concurrencyToken = seed.ConcurrencyToken })).StatusCode);
    }

    [Fact]
    public async Task Operations_reschedule_and_cancel_preserve_history_and_release_the_period()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(pending: false);
        await using (var tokenScope = factory.Services.CreateAsyncScope())
        {
            var tokenDb = tokenScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            tokenDb.CheckInTokens.Add(CheckInToken.Create(seed.ReservationId,
                SHA256.HashData(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow, seed.StartAt.AddHours(1)));
            await tokenDb.SaveChangesAsync();
        }
        await LoginAsync(seed.Manager);
        var newStart = seed.StartAt.AddHours(3);

        var rescheduleResponse = await factory.PostWithCsrfAsync(
            $"/api/admin/reservations/{seed.ReservationId}/reschedule",
            new { startAt = newStart, endAt = newStart.AddHours(1), concurrencyToken = seed.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.Created, rescheduleResponse.StatusCode);
        var replacement = (await rescheduleResponse.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal("RESCHEDULE", replacement.Kind);
        Assert.Equal("APPROVED", replacement.Status);
        Assert.Equal(seed.ReservationId, replacement.OriginalReservationId);
        await AssertStatusAsync(seed.ReservationId, ReservationStatus.Cancelled);
        await using (var tokenScope = factory.Services.CreateAsyncScope())
        {
            var token = await tokenScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CheckInTokens
                .SingleAsync(value => value.ReservationId == seed.ReservationId);
            Assert.NotNull(token.RevokedAt);
        }

        var cancelledResponse = await factory.PostWithCsrfAsync(
            $"/api/admin/reservations/{replacement.Id}/cancel",
            new { concurrencyToken = replacement.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, cancelledResponse.StatusCode);
        Assert.Equal("CANCELLED", (await cancelledResponse.Content.ReadFromJsonAsync<ReservationPayload>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var replacementEntity = await db.Reservations.AsNoTracking().SingleAsync(value => value.Id == replacement.Id);
        var recreate = await factory.PostWithCsrfAsync("/api/admin/reservations", new
        {
            replacementEntity.RoomId,
            replacementEntity.ProfessionalId,
            startAt = newStart,
            endAt = newStart.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Created, recreate.StatusCode);
    }

    private async Task<Seed> SeedAsync(bool pending)
    {
        var owner = await factory.CreateUserAsync($"owner-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var admin = await factory.CreateUserAsync($"admin-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        var manager = await factory.CreateUserAsync($"manager-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Gerente]);
        var now = DateTimeOffset.UtcNow;
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional dono", "Fisioterapia", "+5565999999999", now);
        professional.LinkUser(owner.Id, now);
        var start = now.AddHours(4);
        var reservation = pending
            ? Reservation.RequestNew(room.Id, professional.Id, start, start.AddHours(1), owner.Id, now)
            : Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1), admin.Id, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, professional, reservation);
        await db.SaveChangesAsync();
        return new Seed(owner, admin, manager, reservation.Id, start,
            recepcaototem.Features.Common.ConcurrencyToken.Encode(reservation.Version));
    }

    private async Task LoginAsync(ApplicationUser user) =>
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);

    private async Task AssertStatusAsync(Guid id, ReservationStatus expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var status = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Reservations
            .AsNoTracking().Where(value => value.Id == id).Select(value => value.Status).SingleAsync();
        Assert.Equal(expected, status);
    }

    private sealed record Seed(ApplicationUser Owner, ApplicationUser Admin, ApplicationUser Manager,
        Guid ReservationId, DateTimeOffset StartAt, string ConcurrencyToken);
    private sealed record ReservationPayload(Guid Id, Guid? OriginalReservationId, string Kind,
        string Status, string? RejectionReason, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}
