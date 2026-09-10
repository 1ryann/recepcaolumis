using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GestaoPredio.Application.Customers;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Issue-path coverage for the 6-digit manual check-in code (spec 7A.5 / 7A.7).
/// Partial: Tasks 15 (resolve) and 16 (scripted-source collision/exhaustion) add more files.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed partial class CheckInManualCodeTests(ModulesApiFactory factory)
{
    internal sealed record Issue(string Token, string ManualCode, DateTimeOffset ExpiresAt);

    [Fact]
    public async Task Issue_returns_token_manualCode_and_expiry_and_persists_only_the_keyed_hash()
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        Assert.Matches(new Regex("^\\d{6}$"), issue.ManualCode);
        Assert.NotEqual(issue.ManualCode, issue.Token);
        Assert.True(issue.ExpiresAt > DateTimeOffset.UtcNow, "the issued credential must not already be expired");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
        Assert.True(ManualCheckInCode.TryParse(issue.ManualCode, out var code));

        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
        Assert.Equal(hasher.Hash(code), row.ManualCodeHash);                                             // keyed HMAC (test key)
        Assert.NotEqual(SHA256.HashData(Encoding.ASCII.GetBytes(issue.ManualCode)), row.ManualCodeHash); // not plain SHA-256
        Assert.Equal(32, row.ManualCodeHash!.Length);
        Assert.Null(row.UsedAt);
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task Reissue_rotates_both_representations()
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var first = await ctx.IssueAsync();
        var second = await ctx.IssueAsync();

        Assert.NotEqual(first.ManualCode, second.ManualCode);
        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public async Task Issue_persists_one_row_whose_strong_token_hash_is_the_sha256_preimage_and_differs_from_the_code_hash()
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);

        Assert.Equal(SHA256.HashData(WebEncoders.Base64UrlDecode(issue.Token)), row.TokenHash);
        Assert.Equal(32, row.TokenHash.Length);
        Assert.NotEqual(row.TokenHash, row.ManualCodeHash);
    }

    [Fact]
    public async Task Reissue_leaves_only_the_latest_code_hash_stored()
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var first = await ctx.IssueAsync();
        var second = await ctx.IssueAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
        Assert.True(ManualCheckInCode.TryParse(first.ManualCode, out var firstCode));
        Assert.True(ManualCheckInCode.TryParse(second.ManualCode, out var secondCode));

        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
        Assert.Equal(hasher.Hash(secondCode), row.ManualCodeHash);
        Assert.NotEqual(hasher.Hash(firstCode), row.ManualCodeHash);
        Assert.Null(row.UsedAt);
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task Issue_audit_entry_records_the_reservation_without_the_code_or_any_hash()
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(x => x.TargetEntityId == ctx.ReservationId && x.Action == "CHECK_IN_TOKEN_ISSUED")
            .ToListAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("SUCCEEDED", entry.Result);
        Assert.Equal("RESERVATION", entry.TargetEntityType);
        Assert.Null(entry.ChangedFields);
        Assert.DoesNotContain(issue.ManualCode, entry.CorrelationId, StringComparison.Ordinal);
    }
}

/// <summary>
/// Fixture helpers shared by the manual-code check-in tests (issue + later resolve/collision suites).
/// Modelled on <c>CustomerApiTests.SeedCustomerReservationAsync</c>.
/// </summary>
internal static class CheckInManualCodeFixtureExtensions
{
    public static async Task<CheckInReservationContext> SeedEligibleReservationAsync(this ModulesApiFactory factory)
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();

        const string password = "Valid-Password-123!";
        var user = await factory.CreateUserAsync($"checkin-{Guid.NewGuid():N}@lumis.test", password,
            [SystemRoles.Customer], displayName: "Cliente Check-in");

        var now = DateTimeOffset.UtcNow;
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-10); // check-in window is open now
        var room = Room.Create($"Sala Check-in {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional Check-in", "Fisioterapia",
            $"659{Random.Shared.Next(10_000_000, 99_999_999)}", now);
        var customer = Customer.Create("Cliente Check-in", "+5569988887777", now);
        customer.LinkUser(user.Id, now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, startAt, startAt.AddHours(1),
            user.Id, now, customer.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(room, professional, customer, reservation);
            await db.SaveChangesAsync();
        }

        return new CheckInReservationContext(factory, user.Email!, password, reservation.Id);
    }
}

internal sealed class CheckInReservationContext(ModulesApiFactory factory, string email, string password, Guid reservationId)
{
    public Guid ReservationId => reservationId;

    public async Task<CheckInManualCodeTests.Issue> IssueAsync()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, password)).StatusCode);
        var response = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{reservationId}/check-in-token", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CheckInManualCodeTests.Issue>())!;
    }
}
