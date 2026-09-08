using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalAvailabilityApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Operations_can_replace_custom_week_and_mode_switches_preserve_it()
    {
        await factory.ResetAsync();
        var professional = await SeedProfessionalAsync();
        await SeedOperatingHoursAsync(new(8, 0), new(18, 0));
        await LoginAsync(SystemRoles.Gerente);
        var initial = await GetAsync($"/api/admin/professionals/{professional.Id}/availability");
        Assert.Equal("INHERIT_GLOBAL", initial.Mode);

        var customResponse = await factory.PutWithCsrfAsync(
            $"/api/admin/professionals/{professional.Id}/availability",
            new { mode = "CUSTOM", days = Days((DayOfWeek.Monday, "09:00", "12:00")), concurrencyToken = initial.ConcurrencyToken });
        customResponse.EnsureSuccessStatusCode();
        var custom = (await customResponse.Content.ReadFromJsonAsync<AvailabilityPayload>())!;
        Assert.Single(custom.Days.Single(value => value.DayOfWeek == "MONDAY").Intervals);

        var inheritedResponse = await factory.PutWithCsrfAsync(
            $"/api/admin/professionals/{professional.Id}/availability",
            new { mode = "INHERIT_GLOBAL", concurrencyToken = custom.ConcurrencyToken });
        inheritedResponse.EnsureSuccessStatusCode();
        var inherited = (await inheritedResponse.Content.ReadFromJsonAsync<AvailabilityPayload>())!;
        Assert.Equal("INHERIT_GLOBAL", inherited.Mode);
        Assert.Single(inherited.Days.Single(value => value.DayOfWeek == "MONDAY").Intervals);
        Assert.Equal("08:00", inherited.EffectiveDays.Single(value => value.DayOfWeek == "MONDAY").Intervals[0].StartTime);

        var global = await factory.Client.GetFromJsonAsync<OperatingHoursPayload>("/api/admin/operating-hours");
        var reducedGlobalResponse = await factory.PutWithCsrfAsync("/api/admin/operating-hours",
            new { days = OperatingDays(new(10, 0), new(11, 0)), concurrencyToken = global!.ConcurrencyToken });
        reducedGlobalResponse.EnsureSuccessStatusCode();
        var whileInherited = await GetAsync($"/api/admin/professionals/{professional.Id}/availability");
        Assert.Equal("10:00", whileInherited.EffectiveDays.Single(value => value.DayOfWeek == "MONDAY").Intervals[0].StartTime);
        Assert.Equal("09:00", whileInherited.Days.Single(value => value.DayOfWeek == "MONDAY").Intervals[0].StartTime);

        var restoredResponse = await factory.PutWithCsrfAsync(
            $"/api/admin/professionals/{professional.Id}/availability",
            new { mode = "CUSTOM", concurrencyToken = whileInherited.ConcurrencyToken });
        restoredResponse.EnsureSuccessStatusCode();
        var restored = (await restoredResponse.Content.ReadFromJsonAsync<AvailabilityPayload>())!;
        Assert.Equal("10:00", restored.EffectiveDays.Single(value => value.DayOfWeek == "MONDAY").Intervals[0].StartTime);

        var reducedGlobal = (await reducedGlobalResponse.Content.ReadFromJsonAsync<OperatingHoursPayload>())!;
        (await factory.PutWithCsrfAsync("/api/admin/operating-hours",
            new { days = OperatingDays(new(8, 0), new(18, 0)), concurrencyToken = reducedGlobal.ConcurrencyToken }))
            .EnsureSuccessStatusCode();
        var expanded = await GetAsync($"/api/admin/professionals/{professional.Id}/availability");
        Assert.Equal("09:00", expanded.EffectiveDays.Single(value => value.DayOfWeek == "MONDAY").Intervals[0].StartTime);
        Assert.Equal("12:00", expanded.EffectiveDays.Single(value => value.DayOfWeek == "MONDAY").Intervals[0].EndTime);

        var replacedResponse = await factory.PutWithCsrfAsync(
            $"/api/admin/professionals/{professional.Id}/availability",
            new { mode = "CUSTOM", days = Days((DayOfWeek.Monday, "10:00", "11:00")), concurrencyToken = expanded.ConcurrencyToken });
        replacedResponse.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.ProfessionalAvailabilityIntervals.Where(value =>
            value.ProfessionalId == professional.Id).ToArrayAsync());
    }

    [Fact]
    public async Task Explicit_custom_payload_requires_seven_days_and_current_operating_hours()
    {
        await factory.ResetAsync();
        var professional = await SeedProfessionalAsync();
        await SeedOperatingHoursAsync(new(8, 0), new(18, 0));
        await LoginAsync(SystemRoles.Administrador);
        var current = await GetAsync($"/api/admin/professionals/{professional.Id}/availability");

        var outside = await factory.PutWithCsrfAsync($"/api/admin/professionals/{professional.Id}/availability",
            new { mode = "CUSTOM", days = Days((DayOfWeek.Monday, "07:00", "09:00")), concurrencyToken = current.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);

        var invalidDays = await factory.PutWithCsrfAsync($"/api/admin/professionals/{professional.Id}/availability",
            new { mode = "CUSTOM", days = Days((DayOfWeek.Monday, "09:00", "10:00")).Take(6), concurrencyToken = current.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDays.StatusCode);
    }

    [Fact]
    public async Task Professional_uses_own_link_and_customer_cannot_access_configuration()
    {
        await factory.ResetAsync();
        var email = $"professional-{Guid.NewGuid():N}@lumis.test";
        var user = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        var professional = await SeedProfessionalAsync(user.Id);
        await SeedOperatingHoursAsync(new(8, 0), new(18, 0));
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
        var own = await factory.Client.GetAsync("/api/professional/availability");
        own.EnsureSuccessStatusCode();
        Assert.Equal(professional.Id, (await own.Content.ReadFromJsonAsync<AvailabilityPayload>())!.ProfessionalId);

        await factory.ResetAsync();
        var customerEmail = $"customer-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(customerEmail, Password, [SystemRoles.Customer]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(customerEmail, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/professional/availability")).StatusCode);
    }

    private async Task<Professional> SeedProfessionalAsync(string? userId = null)
    {
        var professional = Professional.Create("Agenda API", "Psicologia",
            $"699{Random.Shared.Next(10000000, 99999999)}", DateTimeOffset.UtcNow);
        if (userId is not null) professional.LinkUser(userId, DateTimeOffset.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        await db.SaveChangesAsync();
        return professional;
    }

    private async Task SeedOperatingHoursAsync(TimeOnly start, TimeOnly end)
    {
        var schedule = OperatingHoursSchedule.Create(DateTimeOffset.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.OperatingHoursSchedules.Add(schedule);
        foreach (var day in Enum.GetValues<DayOfWeek>())
            db.OperatingHourIntervals.AddRange(OperatingHourInterval.CreateDay(schedule.Id, day,
                [new(start, end)]));
        await db.SaveChangesAsync();
    }

    private async Task LoginAsync(string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private async Task<AvailabilityPayload> GetAsync(string path) =>
        (await (await factory.Client.GetAsync(path)).Content.ReadFromJsonAsync<AvailabilityPayload>())!;

    private static object[] Days(params (DayOfWeek Day, string Start, string End)[] configured) =>
        Enum.GetValues<DayOfWeek>().Select(day => new
        {
            dayOfWeek = day.ToString().ToUpperInvariant(),
            intervals = configured.Where(value => value.Day == day)
                .Select(value => new { startTime = value.Start, endTime = value.End }).ToArray()
        }).Cast<object>().ToArray();

    private static object[] OperatingDays(TimeOnly mondayStart, TimeOnly mondayEnd) =>
        Enum.GetValues<DayOfWeek>().Select(day => new
        {
            dayOfWeek = day.ToString().ToUpperInvariant(),
            intervals = day == DayOfWeek.Monday
                ? new object[] { new { opensAt = mondayStart.ToString("HH:mm"), closesAt = mondayEnd.ToString("HH:mm") } }
                : Array.Empty<object>()
        }).Cast<object>().ToArray();

    private sealed record AvailabilityPayload(Guid ProfessionalId, string Mode, DayPayload[] Days,
        DayPayload[] EffectiveDays, string ConcurrencyToken, int ExistingReservationsOutsideAvailabilityCount);
    private sealed record DayPayload(string DayOfWeek, IntervalPayload[] Intervals);
    private sealed record IntervalPayload(string StartTime, string EndTime);
    private sealed record OperatingHoursPayload(bool Configured, string ConcurrencyToken);
}
