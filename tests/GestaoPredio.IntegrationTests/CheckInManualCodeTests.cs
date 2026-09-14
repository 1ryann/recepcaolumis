using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestaoPredio.Application.Customers;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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

    // ---------------------------------------------------------------------------------------------
    // Task 16 — security: HMAC key handling, brute-force limit, collision/reclaim exhaustion, no-leak
    // (spec 7A.5 / 7A.9 / 7A.10 / 7A.11)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The persisted code hash is the keyed HMAC of the app hasher — provably not a plain SHA-256 of the digits.</summary>
    [Fact]
    public async Task Persisted_hash_is_keyed_hmac_not_plain_sha256()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
        Assert.True(ManualCheckInCode.TryParse(issue.ManualCode, out var code));

        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
        Assert.Equal(hasher.Hash(code), row.ManualCodeHash);                                              // matches the app hasher (test key)
        Assert.NotEqual(SHA256.HashData(Encoding.ASCII.GetBytes(issue.ManualCode)), row.ManualCodeHash);  // not plain SHA-256
    }

    /// <summary>
    /// Swap the HMAC key on a fresh host: a previously issued 6-digit code no longer resolves (keyed hash),
    /// while the strong QR token still resolves (its hash is keyless SHA-256) — spec 7A.11 key-loss semantics.
    /// </summary>
    [Fact]
    public async Task A_different_hmac_key_does_not_resolve_previously_issued_codes()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        using var withOtherKey = factory.WithConfig(("CheckIn:ManualCodeHmacKey", "a-totally-different-key"));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await withOtherKey.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode })).StatusCode);
        // ...but the strong QR token still resolves under the new key (hash is keyless)
        Assert.Equal(HttpStatusCode.OK,
            (await withOtherKey.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.Token })).StatusCode);
    }

    /// <summary>
    /// RULING 2 (option B — least brittle): a real Production host with no <c>CheckIn:ManualCodeHmacKey</c>.
    /// <c>HmacManualCheckInCodeHasher</c> is internal to Infrastructure and not visible to this test project,
    /// so a hand-mirrored <c>ServiceCollection</c> cannot name the concrete type; booting the real
    /// <c>Program.cs</c> wiring proves the fail-closed path and that neither the 500 body nor the logs
    /// carry key material. The fail-closed message names the config key only — never a secret value.
    /// </summary>
    [Fact]
    public async Task Missing_hmac_key_in_production_fails_closed()
    {
        var logs = new CapturingLoggerProvider();
        await using var production = new WebApplicationFactory<recepcaototem.Pages.IndexModel>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "");
                builder.UseSetting("AllowedHosts", "localhost");
                builder.UseSetting("Security:DataProtectionPath", Path.Combine(Path.GetTempPath(), "Lumis-Task16-ProdKeys"));
                builder.UseSetting("Storage:PrivateFilesPath", Path.GetTempPath());
                builder.UseSetting("Scheduling:TimeZoneId", "America/Porto_Velho");
                builder.UseSetting("Whatsapp:FinanceiroPhoneNumber", "+5569999999999");
                // deliberately NO CheckIn:ManualCodeHmacKey
                builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs));
            });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        // A 6-digit token forces the endpoint to resolve IManualCheckInCodeHasher, which throws on construction.
        using var response = await client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = "482731" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dev-only", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManualCodeHmacKey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("482731", body, StringComparison.Ordinal);

        var logText = logs.Read(0);
        Assert.Contains("CheckIn:ManualCodeHmacKey", logText, StringComparison.Ordinal);   // the fail-closed error fired...
        foreach (var secretValue in new[]
                 {
                     "dev-only",
                     "integration-tests-manual-code-hmac-key-not-a-secret",
                     "a-totally-different-key"
                 })
            Assert.DoesNotContain(secretValue, logText, StringComparison.OrdinalIgnoreCase); // ...naming the key, never a value
    }

    /// <summary>
    /// Brute-force ceiling (spec 7A.9): from one client, <c>CustomerIpPermitLimit</c> + 1 rapid resolves
    /// with distinct random 6-digit codes — the last one is 429. The shared factory raises
    /// <c>CustomerIpPermitLimit</c> to keep unrelated check-in tests from tripping 429; this test's
    /// isolated host restores the spec §7A.9 default of 30 (and widens the window to 600 s for
    /// determinism) to exercise the brute-force ceiling.
    /// </summary>
    [Fact]
    public async Task Resolve_is_rate_limited_per_ip()
    {
        await factory.ResetAsync();
        using var isolated = factory.WithConfig(("RateLimiting:CustomerWindowSeconds", "600"), ("RateLimiting:CustomerIpPermitLimit", "30"));
        const int ipPermitLimit = 30; // spec 7A.9 default: RateLimiting:CustomerIpPermitLimit

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= ipPermitLimit; i++) // limit + 1 rapid resolves
        {
            var guess = $"{100000 + i:D6}"; // distinct 6-digit codes: the identifier partition never trips first
            using var resp = await isolated.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = guess });
            statuses.Add(resp.StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(ipPermitLimit));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[ipPermitLimit]);
    }

    /// <summary>
    /// §7A.5 exhaustion: a source that yields the live-colliding code five times spends all five attempts
    /// on a resolvable collision → 503 <c>CHECK_IN_CODE_UNAVAILABLE</c>. The error body carries no 6-digit
    /// run and the <c>CHECK_IN_TOKEN_ISSUE_FAILED</c> audit carries neither the code, nor any hash, nor the key.
    /// </summary>
    [Fact]
    public async Task Exhausting_retries_returns_503_without_leaking_value_or_hash()
    {
        await factory.ResetAsync();

        var live = await factory.SeedEligibleReservationAsync();
        var liveIssue = await live.IssueAsync();               // a live, resolvable row to collide with

        var target = await factory.SeedEligibleReservationAsync();
        var lc = liveIssue.ManualCode;
        var (status, body) = await target.IssueRawWithSourceAsync(
            new ScriptedManualCodeSource(lc, lc, lc, lc, lc)); // all 5 attempts collide (the 6th, random, draw is never reached)

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("CHECK_IN_CODE_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\d{6}"), body);        // no 6-digit run leaked in the error body

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
        Assert.True(ManualCheckInCode.TryParse(lc, out var code));

        var failed = await db.AuditEntries.AsNoTracking()
            .Where(x => x.TargetEntityId == target.ReservationId && x.Action == "CHECK_IN_TOKEN_ISSUE_FAILED")
            .ToListAsync();
        Assert.Equal("FAILED", Assert.Single(failed).Result);

        var haystack = AuditHaystack(await db.AuditEntries.AsNoTracking().ToListAsync());
        var testKey = factory.Configuration["CheckIn:ManualCodeHmacKey"]!;
        Assert.DoesNotContain(lc, haystack, StringComparison.Ordinal);
        Assert.DoesNotContain(testKey, haystack, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(hasher.Hash(code)), haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(hasher.Hash(code)), haystack, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spec 7A.10: an issue + resolve + confirm round leaves neither the 6-digit code, nor its keyed hash
    /// (hex or base64), nor the HMAC secret anywhere in <c>AuditEntries</c> or in anything the server logged.
    /// </summary>
    [Fact]
    public async Task Neither_the_code_nor_the_hash_nor_the_secret_appears_in_audit_or_logs()
    {
        await factory.ResetAsync();
        var log = factory.CaptureLogs(); // anchor: everything the server logs from here on

        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode });
        await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.ManualCode });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
        Assert.True(ManualCheckInCode.TryParse(issue.ManualCode, out var code));

        var audit = AuditHaystack(await db.AuditEntries.AsNoTracking().ToListAsync());
        Assert.False(string.IsNullOrEmpty(audit), "resolve/confirm must have written audit rows to check against");
        var testKey = factory.Configuration["CheckIn:ManualCodeHmacKey"]!;
        Assert.Equal("integration-tests-manual-code-hmac-key-not-a-secret", testKey);

        var hex = Convert.ToHexString(hasher.Hash(code));
        var b64 = Convert.ToBase64String(hasher.Hash(code));
        Assert.NotEqual("", log.Text); // the collector observed the same server factory.Client hit — the check is real
        foreach (var haystack in new[] { audit, log.Text })
        {
            Assert.DoesNotContain(issue.ManualCode, haystack, StringComparison.Ordinal);
            Assert.DoesNotContain(testKey, haystack, StringComparison.Ordinal);
            Assert.DoesNotContain(hex, haystack, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(b64, haystack, StringComparison.Ordinal);
        }
    }

    /// <summary>Every column of every audit row, flattened — the search space for "no code / hash / secret leaked".</summary>
    private static string AuditHaystack(IEnumerable<AuditEntry> entries) =>
        string.Join("\n", entries.Select(a => string.Join("|",
            a.Id, a.ActorUserId, a.TargetUserId, a.IpAddress, a.Action, a.Result,
            a.OccurredAt.ToString("O"), a.CorrelationId, a.TargetEntityType, a.TargetEntityId, a.ChangedFields)));
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
/// Test double for the <see cref="IManualCodeSource"/> seam (spec 7A.9). <see cref="Next"/> yields the
/// scripted combinations in order (<c>codes[0]</c>, <c>codes[1]</c>, …); once the script is exhausted it
/// falls back to <see cref="ManualCheckInCode.Generate"/>. Task 16 RULING 4: the exhaustion suite passes
/// the same live-colliding code five times so all five <c>IssueToken</c> attempts collide → 503.
/// </summary>
internal sealed class ScriptedManualCodeSource : IManualCodeSource
{
    private readonly string[] _codes;
    private readonly bool _throwWhenScriptExhausted;
    private int _calls;

    public ScriptedManualCodeSource(params string[] codes) => _codes = codes;

    /// <summary>Task 15 call-site shape: one scripted code, then either random draws or a hard stop.</summary>
    public ScriptedManualCodeSource(string scriptedCode, bool thenFallbackToRandom)
    {
        _codes = [scriptedCode];
        _throwWhenScriptExhausted = !thenFallbackToRandom;
    }

    public ManualCheckInCode Next()
    {
        if (_calls < _codes.Length)
        {
            Assert.True(ManualCheckInCode.TryParse(_codes[_calls++], out var code), "scripted code must be 6 digits");
            return code;
        }

        _calls++;
        if (_throwWhenScriptExhausted)
            throw new InvalidOperationException("ScriptedManualCodeSource was asked past its script but fallback is disabled.");

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
    /// normal <see cref="ModulesApiFactory.Client"/>. Asserts a 200 and returns the issued pair.
    /// </summary>
    public async Task<CheckInManualCodeTests.Issue> IssueWithScriptedCodeAsync(string code, bool thenFallbackToRandom = false)
    {
        var (status, body) = await IssueViaScriptedHostAsync(new ScriptedManualCodeSource(code, thenFallbackToRandom));
        Assert.Equal(HttpStatusCode.OK, status);
        return JsonSerializer.Deserialize<CheckInManualCodeTests.Issue>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>
    /// Issues once through a scripted host, returning the raw status + body without asserting success —
    /// for the §7A.5 exhaustion path where every attempt collides and the endpoint answers 503
    /// (Task 16 RULING 4).
    /// </summary>
    public Task<(HttpStatusCode Status, string Body)> IssueRawWithSourceAsync(IManualCodeSource source) =>
        IssueViaScriptedHostAsync(source);

    private async Task<(HttpStatusCode Status, string Body)> IssueViaScriptedHostAsync(IManualCodeSource source)
    {
        using var scriptedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IManualCodeSource>();
            services.AddSingleton(source);
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
        return (issueResponse.StatusCode, await issueResponse.Content.ReadAsStringAsync());
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
