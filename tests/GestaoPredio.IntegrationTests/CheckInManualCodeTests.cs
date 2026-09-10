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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using recepcaototem.Features.Customers;
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
        await factory.ResetAsync();
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
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var first = await ctx.IssueAsync();
        var second = await ctx.IssueAsync();

        Assert.NotEqual(first.ManualCode, second.ManualCode);
        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public async Task Issue_persists_one_row_whose_strong_token_hash_is_the_sha256_preimage_and_differs_from_the_code_hash()
    {
        await factory.ResetAsync();
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
        await factory.ResetAsync();
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
        await factory.ResetAsync();
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

    // ---------------------------------------------------------------------------------------------
    // Task 15 — resolve/confirm dispatch by string shape onto the single eligibility rule (spec 7A.6)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Manual_code_resolves_and_confirms_like_the_qr_token()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        var preview = await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        var confirm = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.ManualCode });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        // idempotent second confirm — QR side; the manual code hash is reclaimed by MarkUsed
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token })).StatusCode);
    }

    [Fact]
    public async Task Consuming_via_manual_invalidates_the_qr_token_and_vice_versa()
    {
        await factory.ResetAsync();
        var a = await factory.SeedEligibleReservationAsync();
        var ia = await a.IssueAsync();
        await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = ia.ManualCode });
        // consuming via the manual code invalidates the QR token (UsedAt gate)
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = ia.Token })).StatusCode);

        var b = await factory.SeedEligibleReservationAsync();
        var ib = await b.IssueAsync();
        await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = ib.Token });
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = ib.ManualCode })).StatusCode);
    }

    [Fact]
    public async Task Reissue_kills_the_previous_pair()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var first = await ctx.IssueAsync();
        var second = await ctx.IssueAsync();
        foreach (var stale in new[] { first.ManualCode, first.Token })
            Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = stale })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = second.ManualCode })).StatusCode);
    }

    [Theory]
    [InlineData("000000")]   // random miss
    [InlineData("123456")]
    public async Task Unknown_manual_code_fails_generically(string guess)
    {
        await factory.ResetAsync();
        var response = await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = guess });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Não foi possível validar este código", body);
        Assert.DoesNotContain("reservation", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Consuming_or_revoking_nulls_the_manual_code_hash_keeping_the_token_hash()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var i1 = await ctx.IssueAsync();
        await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = i1.ManualCode });
        await using (var s = factory.Services.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
            Assert.Null(row.ManualCodeHash);
            Assert.NotNull(row.UsedAt);
            Assert.Equal(32, row.TokenHash.Length);
        }

        var ctx2 = await factory.SeedEligibleReservationAsync();
        await ctx2.IssueAsync();
        await ctx2.CancelAsync();
        await using (var s = factory.Services.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx2.ReservationId);
            Assert.Null(row.ManualCodeHash);
            Assert.NotNull(row.RevokedAt);
        }
    }

    [Fact]
    public async Task Cancelling_the_reservation_invalidates_both_and_frees_the_code()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        await ctx.CancelAsync();
        foreach (var stale in new[] { issue.ManualCode, issue.Token })
            Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = stale })).StatusCode);

        // the 6-digit combination is reusable now: script the source to produce it for another reservation.
        var other = await factory.SeedEligibleReservationAsync();
        var reissued = await other.IssueWithScriptedCodeAsync(issue.ManualCode);
        Assert.Equal(issue.ManualCode, reissued.ManualCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = reissued.ManualCode })).StatusCode);
    }

    [Fact]
    public async Task Rescheduling_invalidates_the_old_credential_and_the_new_reservation_has_none()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        var newReservationId = await ctx.RescheduleAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.CheckInTokens.AnyAsync(x => x.ReservationId == newReservationId));
    }

    [Fact]
    public async Task Issue_reclaims_an_expired_stale_row_but_never_a_resolvable_one()
    {
        await factory.ResetAsync();
        // stale (expired, not used/revoked, still has ManualCodeHash) — script the new issue to the same code
        var stale = await factory.SeedIssuedThenExpiredAsync();          // helper: issue, then set ExpiresAt in the past directly in the DB
        var fresh = await factory.SeedEligibleReservationAsync();
        var reissued = await fresh.IssueWithScriptedCodeAsync(stale.ManualCode);
        Assert.Equal(stale.ManualCode, reissued.ManualCode);             // reclaimed
        await using (var s = factory.Services.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.CheckInTokens.SingleAsync(x => x.ReservationId == stale.ReservationId)).ManualCodeHash);
        }

        // resolvable row — the new issue must pick a DIFFERENT code, leaving the other row untouched
        var live = await factory.SeedEligibleReservationAsync();
        var liveIssue = await live.IssueAsync();
        var another = await factory.SeedEligibleReservationAsync();
        var scripted = await another.IssueWithScriptedCodeAsync(liveIssue.ManualCode, thenFallbackToRandom: true);
        Assert.NotEqual(liveIssue.ManualCode, scripted.ManualCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = liveIssue.ManualCode })).StatusCode);
    }
}

/// <summary>
/// Fixture helpers shared by the manual-code check-in tests (issue + later resolve/collision suites).
/// Modelled on <c>CustomerApiTests.SeedCustomerReservationAsync</c>.
/// </summary>
internal static class CheckInManualCodeFixtureExtensions
{
    /// <summary>
    /// Seeds one eligible (Approved, window-open) reservation with a linked customer user.
    /// Does NOT reset the database: multi-seed tests (brief #6 / #8) need several rows to
    /// coexist. Each test body resets first (spec 7A / RULING 6). Every seed uses unique
    /// room / professional / customer-phone / user-email so calls never collide.
    /// </summary>
    public static async Task<CheckInReservationContext> SeedEligibleReservationAsync(this ModulesApiFactory factory)
    {
        await factory.SeedDefaultOperatingHoursAsync();

        const string password = "Valid-Password-123!";
        var user = await factory.CreateUserAsync($"checkin-{Guid.NewGuid():N}@lumis.test", password,
            [SystemRoles.Customer], displayName: "Cliente Check-in");

        var now = DateTimeOffset.UtcNow;
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-10); // check-in window is open now
        var room = Room.Create($"Sala Check-in {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional Check-in", "Fisioterapia",
            $"659{Random.Shared.Next(10_000_000, 99_999_999)}", now);
        var customer = Customer.Create("Cliente Check-in", $"+5569{Random.Shared.Next(900_000_000, 999_999_999)}", now);
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

    /// <summary>
    /// Seeds an eligible reservation, issues the credential, then pushes <c>ExpiresAt</c> into the
    /// past with a direct DB update (leaves <c>RevokedAt</c>/<c>UsedAt</c> null and <c>ManualCodeHash</c>
    /// non-null) — a stale row that no longer resolves but still pins its 6-digit combination until a
    /// later issue lazily reclaims it (spec 7A.5 step 6).
    /// </summary>
    public static async Task<StaleCheckInContext> SeedIssuedThenExpiredAsync(this ModulesApiFactory factory)
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.CheckInTokens
            .Where(x => x.ReservationId == ctx.ReservationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)));

        return new StaleCheckInContext(issue.ManualCode, ctx.ReservationId);
    }
}

/// <summary>Minimal view of a stale (issued-then-expired) credential for brief test #8.</summary>
internal sealed record StaleCheckInContext(string ManualCode, Guid ReservationId);

/// <summary>
/// Test double for the <see cref="IManualCodeSource"/> seam (spec 7A.9). The first <see cref="Next"/>
/// returns the scripted 6-digit combination; later calls either fall back to a random draw or throw,
/// per <paramref name="thenFallbackToRandom"/>.
/// </summary>
internal sealed class ScriptedManualCodeSource(string scriptedCode, bool thenFallbackToRandom) : IManualCodeSource
{
    private int _calls;

    public ManualCheckInCode Next()
    {
        if (_calls++ == 0)
        {
            Assert.True(ManualCheckInCode.TryParse(scriptedCode, out var code), "scripted code must be 6 digits");
            return code;
        }

        if (!thenFallbackToRandom)
            throw new InvalidOperationException("ScriptedManualCodeSource was asked for a second code but fallback is disabled.");

        return ManualCheckInCode.Generate();
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

    /// <summary>
    /// Issues the credential once with a scripted <see cref="IManualCodeSource"/> swapped into DI
    /// for this call only (RULING 3). Uses a throw-away <see cref="WebApplicationFactory{T}"/> layered
    /// over the shared config (same Postgres schema, same clock); later resolve/confirm run on the
    /// normal <see cref="ModulesApiFactory.Client"/>.
    /// </summary>
    public async Task<CheckInManualCodeTests.Issue> IssueWithScriptedCodeAsync(string code, bool thenFallbackToRandom = false)
    {
        using var scriptedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IManualCodeSource>();
            services.AddSingleton<IManualCodeSource>(new ScriptedManualCodeSource(code, thenFallbackToRandom));
        }));
        using var scriptedClient = scriptedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        loginRequest.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(scriptedClient));
        Assert.Equal(HttpStatusCode.NoContent, (await scriptedClient.SendAsync(loginRequest)).StatusCode);

        using var issueRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/customer/reservations/{reservationId}/check-in-token")
        {
            Content = JsonContent.Create(new { })
        };
        issueRequest.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(scriptedClient));
        using var issueResponse = await scriptedClient.SendAsync(issueRequest);
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        return (await issueResponse.Content.ReadFromJsonAsync<CheckInManualCodeTests.Issue>())!;
    }

    /// <summary>Cancels the reservation as its customer (frees the credential via <c>Revoke</c>).</summary>
    public async Task CancelAsync()
    {
        var detail = await CurrentAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{reservationId}/cancel",
            new { concurrencyToken = detail.ConcurrencyToken })).StatusCode);
    }

    /// <summary>Reschedules the reservation as its customer; returns the replacement reservation id.</summary>
    public async Task<Guid> RescheduleAsync()
    {
        var detail = await CurrentAsync();
        var start = detail.StartAt.AddHours(2);
        var response = await factory.PostWithCsrfAsync(
            $"/api/customer/reservations/{reservationId}/reschedule",
            new { professionalId = detail.ProfessionalId, startAt = start, endAt = start.AddHours(1), concurrencyToken = detail.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReservationDto>())!.Id;
    }

    private async Task<ReservationDto> CurrentAsync()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, password)).StatusCode);
        using var response = await factory.Client.GetAsync($"/api/customer/reservations/{reservationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReservationDto>())!;
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CsrfDto>())!.Token;
    }

    private sealed record CsrfDto(string Token);
    private sealed record ReservationDto(Guid Id, Guid ProfessionalId, DateTimeOffset StartAt, string ConcurrencyToken);
}
