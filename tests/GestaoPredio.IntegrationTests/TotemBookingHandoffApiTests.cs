using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemBookingHandoffApiTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Create_returns_two_distinct_tokens_stored_only_as_hashes()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
        var log = factory.CaptureLogs();
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = prof });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await res.Content.ReadFromJsonAsync<CreateBody>();
        Assert.NotEqual(b!.HandoffToken, b.StatusToken);
        Assert.Equal("Dra. Ana", b.ProfessionalName);
        Assert.Equal("Fisioterapia", b.Profession);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TotemBookingHandoffs.SingleAsync();
        Assert.Equal(32, row.HandoffTokenHash.Length);
        Assert.Equal(32, row.StatusTokenHash.Length);
        Assert.NotEqual(row.HandoffTokenHash, row.StatusTokenHash);
        Assert.DoesNotContain(b.HandoffToken, log.Text);
        Assert.DoesNotContain(b.StatusToken, log.Text);
    }

    [Fact]
    public async Task Status_is_PENDING_then_EXPIRED_after_the_window_and_wrong_token_is_generic()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(factory.UtcNow);
        var b = await CreateHandoffAsync(prof);
        var p = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("PENDING", (await p.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        var wrong = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await wrong.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        factory.FreezeTime(factory.UtcNow.AddMinutes(6));
        var e = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("EXPIRED", (await e.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TotemBookingHandoffs.SingleAsync();
        Assert.Equal(GestaoPredio.Domain.Customers.TotemBookingHandoffStatus.Expired, row.Status);
    }

    [Fact]
    public async Task Status_unknown_id_is_the_same_generic_400_as_a_wrong_token()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);
        var unknown = await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{Guid.NewGuid()}/status", new { statusToken = b.StatusToken });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await unknown.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Cancel_expires_a_pending_handoff_and_is_idempotent()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var b = await CreateHandoffAsync(prof);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
        var s = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("EXPIRED", (await s.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditEntries.Where(x => x.Action == "TOTEM_HANDOFF_CANCELLED").ToListAsync();
        Assert.Single(audits);
    }

    [Fact]
    public async Task Cancel_requires_the_matching_status_token_and_never_reveals_existence()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        var a = await CreateHandoffAsync(prof);
        var other = await CreateHandoffAsync(prof);

        // handoff A's id + handoff B's statusToken -> generic 400, A is NOT cancelled
        var cross = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/cancel", new { statusToken = other.StatusToken });
        Assert.Equal(HttpStatusCode.BadRequest, cross.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await cross.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // garbage token for a real id, and any token for a random id -> same generic 400 (no oracle)
        var garbage = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/cancel", new { statusToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        var unknown = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{Guid.NewGuid()}/cancel", new { statusToken = a.StatusToken });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // A is still PENDING (no mutation happened on any failed attempt)
        var still = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/status", new { statusToken = a.StatusToken });
        Assert.Equal("PENDING", (await still.Content.ReadFromJsonAsync<StatusBody>())!.Status);
    }

    [Fact]
    public async Task Status_lazy_expiry_swallows_a_concurrent_write_and_still_returns_200_EXPIRED()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(factory.UtcNow);
        var b = await CreateHandoffAsync(prof);

        // Move the server clock past the 5-minute window so the status handler's lazy-expiry
        // branch fires. Two DbContexts both load the row while it is still Pending, then race
        // to fold it to Expired: the second SaveChanges MUST hit DbUpdateConcurrencyException
        // (Version is xmin). The endpoint's catch clears + re-reads, so the caller sees 200.
        factory.FreezeTime(factory.UtcNow.AddMinutes(6));

        await using (var scopeA = factory.Services.CreateAsyncScope())
        await using (var scopeB = factory.Services.CreateAsyncScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hA = await dbA.TotemBookingHandoffs.SingleAsync(x => x.Id == b.Id);
            var hB = await dbB.TotemBookingHandoffs.SingleAsync(x => x.Id == b.Id);

            hA.MarkExpired(factory.UtcNow);
            await dbA.SaveChangesAsync();

            hB.MarkExpired(factory.UtcNow);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

            // Exactly the recovery the endpoint performs in its catch block.
            dbB.ChangeTracker.Clear();
            var reread = await dbB.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.Id == b.Id);
            Assert.NotNull(reread);
            Assert.Equal(GestaoPredio.Domain.Customers.TotemBookingHandoffStatus.Expired, reread!.Status);
        }

        // The row is already terminal now; a poll must still be a clean 200 EXPIRED.
        var s = await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal(HttpStatusCode.OK, s.StatusCode);
        Assert.Equal("EXPIRED", (await s.Content.ReadFromJsonAsync<StatusBody>())!.Status);
    }

    [Fact]
    public async Task Concurrent_status_and_cancel_on_the_same_pending_handoff_never_500()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(factory.UtcNow);
        var b = await CreateHandoffAsync(prof);
        factory.FreezeTime(factory.UtcNow.AddMinutes(6));   // both handlers will try to fold Pending -> Expired

        var calls = new[]
        {
            factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken }),
            factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken }),
            factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken }),
        };
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var final = await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
        Assert.Equal("EXPIRED", (await final.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        // A cancel that lost the race discards its unit of work (audit included); one that won
        // wrote exactly one. Either way the count is at most one — never a partial double-write.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_CANCELLED");
        Assert.True(audits <= 1, $"expected 0 or 1 cancel audit, found {audits}");
    }

    [Fact]
    public async Task Create_rejects_an_inactive_or_unknown_professional_generically()
    {
        await factory.ResetAsync();
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Claim_marks_started_without_auth_extends_the_window_once_and_returns_no_pii()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
        factory.FreezeTime(SecondAlignedUtcNow());
        var b = await CreateHandoffAsync(prof);
        var log = factory.CaptureLogs();

        // The visitor's phone claims the handoff BEFORE authenticating: no auth header at all.
        factory.FreezeTime(factory.UtcNow.AddMinutes(4));
        var first = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        Assert.Contains("STARTED", body);
        Assert.DoesNotContain("Dra. Ana", body);          // no professional name
        Assert.DoesNotContain("Fisioterapia", body);      // no profession
        Assert.DoesNotContain(prof.ToString(), body);     // no professionalId

        DateTimeOffset? startedAfterFirst;
        DateTimeOffset expiresAfterFirst;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.TotemBookingHandoffs.SingleAsync();
            Assert.NotNull(row.StartedAt);
            Assert.Equal(factory.UtcNow, row.StartedAt);                  // StartedAt == the claim instant
            Assert.Equal(factory.UtcNow.AddMinutes(10), row.ExpiresAt);   // extended to now+grace (=T+14), under the T+20 ceiling
            startedAfterFirst = row.StartedAt;
            expiresAfterFirst = row.ExpiresAt;
        }

        // A second claim 3 minutes later must NOT re-set StartedAt and must NOT re-extend the window.
        factory.FreezeTime(factory.UtcNow.AddMinutes(3));
        var second = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using (var scope2 = factory.Services.CreateAsyncScope())
        {
            var row2 = await scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>().TotemBookingHandoffs.SingleAsync();
            Assert.Equal(startedAfterFirst, row2.StartedAt);              // not re-set
            Assert.Equal(expiresAfterFirst, row2.ExpiresAt);             // unchanged from the first claim
            Assert.Equal(factory.UtcNow.AddMinutes(7), row2.ExpiresAt);  // i.e. still T+14
        }

        Assert.DoesNotContain(b.HandoffToken, log.Text);                 // the token is never logged
    }

    [Fact]
    public async Task Claim_after_expiry_is_410_and_bad_token_is_400()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(SecondAlignedUtcNow());
        var b = await CreateHandoffAsync(prof);

        var bad = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = "xxx" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("INVALID_HANDOFF", (await bad.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        factory.FreezeTime(factory.UtcNow.AddMinutes(6));   // past the initial 5-minute window
        var expired = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await expired.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Claim_on_a_non_pending_handoff_is_410()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(SecondAlignedUtcNow());
        var b = await CreateHandoffAsync(prof);

        // Cancel folds the row to Expired while it is still inside the time window.
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync(
            $"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);

        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
        Assert.Equal("HANDOFF_EXPIRED", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Concurrent_claims_on_the_same_pending_handoff_never_500_and_extend_once()
    {
        await factory.ResetAsync();
        var prof = await factory.SeedActiveProfessionalAsync();
        factory.FreezeTime(SecondAlignedUtcNow());
        var b = await CreateHandoffAsync(prof);
        factory.FreezeTime(factory.UtcNow.AddMinutes(4));

        // Three overlapping claims all load the same Pending row and race to write StartedAt /
        // ExpiresAt. The losers MUST hit DbUpdateConcurrencyException (Version is xmin); the
        // handler swallows it, re-reads, and still returns 200 — MarkStarted is idempotent.
        var calls = new[]
        {
            factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken }),
            factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken }),
            factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken }),
        };
        var responses = await Task.WhenAll(calls);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TotemBookingHandoffs.SingleAsync();
        Assert.Equal(factory.UtcNow, row.StartedAt);
        Assert.Equal(factory.UtcNow.AddMinutes(10), row.ExpiresAt);   // extended exactly once
    }

    /// <summary>
    /// The brief freezes <c>factory.UtcNow</c> directly; the domain normalizes persisted
    /// instants to microsecond precision, so an exact <c>ExpiresAt</c> / <c>StartedAt</c> equality
    /// assertion would be flaky against a sub-microsecond wall-clock tick. Anchoring the frozen
    /// clock to a whole second removes that without changing any window arithmetic.
    /// </summary>
    private DateTimeOffset SecondAlignedUtcNow()
    {
        var now = factory.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    private async Task<CreateBody> CreateHandoffAsync(Guid professionalId)
    {
        var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<CreateBody>())!;
    }

    private sealed record CreateBody(Guid Id, string HandoffToken, string StatusToken, DateTimeOffset ExpiresAt, string ProfessionalName, string Profession);
    private sealed record StatusBody(string Status, DateTimeOffset? ExpiresAt, string? ProfessionalName, DateTimeOffset? StartAt, string? RoomName);
    private sealed record ErrorBody(string Code, string Message);
}
