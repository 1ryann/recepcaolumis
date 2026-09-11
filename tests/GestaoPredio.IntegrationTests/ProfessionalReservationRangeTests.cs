using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalReservationRangeTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Professional_reservations_can_be_filtered_to_a_day_window_with_exact_total()
    {
        await factory.ResetAsync();
        var seed = await SeedProfessionalWithReservationsAsync(todayCount: 3, otherDayCount: 5);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, Password)).StatusCode);

        var from = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var to = from.AddDays(1);
        var page = await factory.Client.GetFromJsonAsync<PagedReservations>(
            $"/api/professional/reservations?status=all&from={Iso(from)}&to={Iso(to)}&page=1&pageSize=1");

        Assert.NotNull(page);
        Assert.Equal(3, page!.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Professional_reservations_reject_an_inverted_day_window()
    {
        await factory.ResetAsync();
        var seed = await SeedProfessionalWithReservationsAsync(todayCount: 1, otherDayCount: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, Password)).StatusCode);

        var to = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var from = to.AddDays(1);
        var response = await factory.Client.GetAsync(
            $"/api/professional/reservations?from={Iso(from)}&to={Iso(to)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_DATE_RANGE", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Professional_reservations_without_a_window_return_the_full_total()
    {
        await factory.ResetAsync();
        var seed = await SeedProfessionalWithReservationsAsync(todayCount: 3, otherDayCount: 5);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, Password)).StatusCode);

        var page = await factory.Client.GetFromJsonAsync<PagedReservations>(
            "/api/professional/reservations?status=all&page=1&pageSize=1");

        Assert.NotNull(page);
        Assert.Equal(8, page!.TotalCount);
    }

    private static string Iso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private async Task<SeededProfessional> SeedProfessionalWithReservationsAsync(int todayCount, int otherDayCount)
    {
        var user = await factory.CreateUserAsync($"prof-range-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var now = DateTimeOffset.UtcNow;
        var professional = Professional.Create("Profissional range", "Fisioterapia", "+5565999999999", now);
        professional.LinkUser(user.Id, now);
        var room = Room.Create($"Sala range {Guid.NewGuid():N}", null, 10m, 50m, now);

        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var reservations = new List<Reservation>();
        for (var i = 0; i < todayCount; i++)
        {
            var start = today.AddHours(6 + i);
            reservations.Add(Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1),
                user.Id, now));
        }
        for (var i = 0; i < otherDayCount; i++)
        {
            var start = today.AddDays(4).AddHours(6 + i);
            reservations.Add(Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1),
                user.Id, now));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Add(professional);
        db.Add(room);
        db.AddRange(reservations);
        await db.SaveChangesAsync();
        return new SeededProfessional(user.Email!, professional.Id);
    }

    private sealed record SeededProfessional(string Email, Guid ProfessionalId);
    private sealed record PagedReservations(IReadOnlyList<ReservationRow> Items, int Page, int PageSize, int TotalCount);
    private sealed record ReservationRow(Guid Id);
    private sealed record ErrorPayload(string Code, string Message);
}
