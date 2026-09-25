using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Domain.Finance;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class LeaseAdministrationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Operations_can_create_and_query_a_lease_with_occurrence_and_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.Equal("AGENDADA", created.Status);
        Assert.NotEmpty(created.ConcurrencyToken);
        var page = (await (await factory.Client.GetAsync("/api/admin/leases?status=AGENDADA"))
            .Content.ReadFromJsonAsync<LeasePage>())!;
        Assert.Single(page.Items);
        Assert.Equal(HttpStatusCode.OK,
            (await factory.Client.GetAsync($"/api/admin/leases/{created.Id}")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.LeaseOccurrences.ToListAsync());
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
    }

    [Fact]
    public async Task Creation_without_room_rental_inquiry_id_field_behaves_identically_to_a_plain_lease()
    {
        // Task 13 extension point: the request accepts an optional trailing RoomRentalInquiryId. A
        // request that never mentions it at all must behave byte-for-byte like before the field existed.
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.Equal("AGENDADA", created.Status);
        Assert.NotEmpty(created.ConcurrencyToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.LeaseOccurrences.ToListAsync());
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_RENTAL_INQUIRY_CONVERTED"));
    }

    [Fact]
    public async Task Overlapping_room_or_professional_is_rejected_without_success_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();

        var conflict = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("LEASE_RESOURCE_CONFLICT", (await conflict.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
    }

    [Fact]
    public async Task Creation_requires_operations_authorization_csrf_and_strict_json()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).StatusCode);

        await LoginAsync(SystemRoles.Profissional);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).StatusCode);

        await factory.ResetAsync();
        resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/admin/leases", Body(resources))).StatusCode);

        var body = Body(resources);
        var extra = new
        {
            body.TenantId, body.ProfessionalId, body.RoomId, body.Mode, body.ContractedRate,
            body.BillingStartAt, body.BillingDueDay, body.OccupancyStartAt, body.OccupancyEndAt,
            lifecycleState = "ENDED"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync("/api/admin/leases", extra)).StatusCode);
    }

    [Fact]
    public async Task Inactive_resource_is_rejected_and_search_filters_in_the_database()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();

        var matching = (await (await factory.Client.GetAsync("/api/admin/leases?search=profissional%20teste&roomId=" + resources.Room.Id))
            .Content.ReadFromJsonAsync<LeasePage>())!;
        Assert.Single(matching.Items);
        var missing = (await (await factory.Client.GetAsync("/api/admin/leases?search=inexistente"))
            .Content.ReadFromJsonAsync<LeasePage>())!;
        Assert.Empty(missing.Items);

        await factory.ResetAsync();
        resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = await db.Rooms.SingleAsync(x => x.Id == resources.Room.Id);
        room.Deactivate(factory.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).StatusCode);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("professionalId")]
    [InlineData("roomId")]
    public async Task Empty_resource_filter_is_rejected(string filter)
    {
        await factory.ResetAsync();
        await LoginAsync(SystemRoles.Administrador);

        var response = await factory.Client.GetAsync(
            $"/api/admin/leases?{filter}=00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_RESOURCE_FILTER", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Scheduled_lease_can_be_updated_and_stale_token_is_rejected_without_success_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources)))
            .Content.ReadFromJsonAsync<LeasePayload>())!;
        var body = Body(resources);
        var update = new
        {
            body.TenantId, body.ProfessionalId, body.RoomId, body.Mode,
            contractedRate = 175.25m, body.BillingStartAt, body.BillingDueDay,
            body.OccupancyStartAt, body.OccupancyEndAt,
            concurrencyToken = created.ConcurrencyToken
        };

        var success = await factory.PutWithCsrfAsync($"/api/admin/leases/{created.Id}", update);
        success.EnsureSuccessStatusCode();
        var updated = (await success.Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);

        var stale = await factory.PutWithCsrfAsync($"/api/admin/leases/{created.Id}", update);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_UPDATED"));
    }

    [Fact]
    public async Task Postpone_preserves_billing_start_and_cancel_ends_a_scheduled_lease()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources)))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        var postponedStart = created.OccupancyStartAt.AddMinutes(30);
        var postponed = await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/postpone-occupancy", new
        {
            occupancyStartAt = postponedStart,
            concurrencyToken = created.ConcurrencyToken
        });
        postponed.EnsureSuccessStatusCode();
        var afterPostpone = (await postponed.Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        Assert.Equal(created.BillingStartAt, afterPostpone.BillingStartAt);
        Assert.Equal(postponedStart, afterPostpone.OccupancyStartAt);

        var cancelled = await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/cancel", new
        {
            concurrencyToken = afterPostpone.ConcurrencyToken
        });
        cancelled.EnsureSuccessStatusCode();
        Assert.Equal("CANCELADA", (await cancelled.Content.ReadFromJsonAsync<LeasePayload>())!.Status);
    }

    [Fact]
    public async Task Active_lease_supports_scheduled_and_immediate_end_with_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        var now = factory.UtcNow;
        var lease = Lease.Create(resources.Tenant.Id, resources.Professional.Id, resources.Room.Id,
            LeaseMode.Hourly, 120m, now.AddDays(-2), null, now.AddHours(-1), now.AddHours(3), null, now.AddDays(-2));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Leases.Add(lease);
            db.LeaseOccurrences.Add(LeaseOccurrence.Create(lease.Id, lease.OccupancyStartAt, lease.OccupancyEndAt!.Value, now.AddDays(-2)));
            await db.SaveChangesAsync();
        }
        await LoginAsync(SystemRoles.Administrador);
        var current = (await (await factory.Client.GetAsync($"/api/admin/leases/{lease.Id}"))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        var scheduled = await factory.PostWithCsrfAsync($"/api/admin/leases/{lease.Id}/end", new
        {
            endAt = now.AddHours(1), concurrencyToken = current.ConcurrencyToken
        });
        scheduled.EnsureSuccessStatusCode();
        var afterSchedule = (await scheduled.Content.ReadFromJsonAsync<LeaseDetailPayload>())!;

        var ended = await factory.PostWithCsrfAsync($"/api/admin/leases/{lease.Id}/end", new
        {
            endAt = (DateTimeOffset?)null, concurrencyToken = afterSchedule.ConcurrencyToken
        });
        ended.EnsureSuccessStatusCode();
        Assert.Equal("ENCERRADA", (await ended.Content.ReadFromJsonAsync<LeasePayload>())!.Status);
        await using var verification = factory.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await verificationDb.AuditEntries.CountAsync(x => x.Action == "LEASE_END_SCHEDULED"));
        Assert.Equal(1, await verificationDb.AuditEntries.CountAsync(x => x.Action == "LEASE_ENDED"));
    }

    [Fact]
    public async Task Scheduled_end_cannot_extend_into_another_lease()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        var now = factory.UtcNow;
        var current = Lease.Create(resources.Tenant.Id, resources.Professional.Id, resources.Room.Id,
            LeaseMode.Hourly, 100m, now.AddDays(-1), null, now.AddHours(-1), now.AddHours(1), null, now.AddDays(-1));
        var future = Lease.Create(resources.Tenant.Id, resources.Professional.Id, resources.Room.Id,
            LeaseMode.Hourly, 100m, now, null, now.AddHours(2), now.AddHours(4), null, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Leases.AddRange(current, future);
            await db.SaveChangesAsync();
        }
        await LoginAsync(SystemRoles.Administrador);
        var detail = (await (await factory.Client.GetAsync($"/api/admin/leases/{current.Id}"))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;

        var response = await factory.PostWithCsrfAsync($"/api/admin/leases/{current.Id}/end", new
        {
            endAt = now.AddHours(3), concurrencyToken = detail.ConcurrencyToken
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LEASE_RESOURCE_CONFLICT", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Creation_reconciles_an_expired_lease_before_reusing_its_resources()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        var now = factory.UtcNow;
        var expired = Lease.Create(resources.Tenant.Id, resources.Professional.Id, resources.Room.Id,
            LeaseMode.Hourly, 100m, now.AddDays(-2), null, now.AddDays(-1).AddHours(-1), now.AddDays(-1), null, now.AddDays(-2));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Leases.Add(expired);
            await db.SaveChangesAsync();
        }
        await LoginAsync(SystemRoles.Administrador);

        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();

        await using var verification = factory.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(LeaseLifecycleState.Ended, (await verificationDb.Leases.SingleAsync(x => x.Id == expired.Id)).LifecycleState);
        Assert.Equal(1, await verificationDb.AuditEntries.CountAsync(x => x.TargetEntityId == expired.Id && x.Action == "LEASE_ENDED"));
    }

    // Reception asked to be able to undo a cancellation or an ending instead of retyping the contract.
    [Fact]
    public async Task A_cancelled_lease_is_reactivated_under_a_new_end_and_plans_its_occurrence_again()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources)))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        var cancelled = (await (await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/cancel",
            new { concurrencyToken = created.ConcurrencyToken })).Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.Equal("CANCELADA", cancelled.Status);

        var newEnd = factory.UtcNow.AddDays(3);
        var response = await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/reactivate",
            new { occupancyEndAt = newEnd, concurrencyToken = cancelled.ConcurrencyToken });

        response.EnsureSuccessStatusCode();
        var reactivated = (await response.Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        Assert.Equal("AGENDADA", reactivated.Status);
        Assert.Equal(created.OccupancyStartAt, reactivated.OccupancyStartAt);
        Assert.Equal(newEnd, reactivated.OccupancyEndAt);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // The occurrence cancelled on the way out is replaced by a planned one: the room is taken again.
        var occurrence = Assert.Single(await db.LeaseOccurrences.Where(x => x.LeaseId == created.Id).ToListAsync());
        Assert.Equal(LeaseOccurrenceState.Planned, occurrence.State);
        Assert.Equal(newEnd, occurrence.EndAt);
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_REACTIVATED"));
    }

    [Fact]
    public async Task Reactivation_refuses_a_past_end_and_a_period_taken_meanwhile()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources)))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        var cancelled = (await (await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/cancel",
            new { concurrencyToken = created.ConcurrencyToken })).Content.ReadFromJsonAsync<LeasePayload>())!;

        var past = await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/reactivate",
            new { occupancyEndAt = factory.UtcNow.AddHours(-1), concurrencyToken = cancelled.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal("INVALID_LEASE", (await past.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        // The room did not stay empty while the lease was cancelled.
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();
        var taken = await factory.PostWithCsrfAsync($"/api/admin/leases/{created.Id}/reactivate",
            new { occupancyEndAt = factory.UtcNow.AddDays(3), concurrencyToken = cancelled.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
    }

    [Fact]
    public async Task A_lease_without_charges_is_deleted_with_its_occurrences_and_audited()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources)))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;

        var response = await factory.DeleteWithCsrfAsync($"/api/admin/leases/{created.Id}",
            new { concurrencyToken = created.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Leases.SingleOrDefaultAsync(x => x.Id == created.Id));
        Assert.Empty(await db.LeaseOccurrences.Where(x => x.LeaseId == created.Id).ToListAsync());
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_DELETED"));
        // The room is free again: the same period can be let to someone else.
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_lease_that_already_produced_a_charge_is_not_deleted()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var created = (await (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources)))
            .Content.ReadFromJsonAsync<LeaseDetailPayload>())!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.FinancialCharges.Add(FinancialCharge.Create(created.Id, resources.Professional.Id, resources.Tenant.Id,
                factory.UtcNow.AddDays(-2), factory.UtcNow.AddDays(-1),
                DateOnly.FromDateTime(factory.UtcNow.AddDays(5).DateTime), 150.50m, "mensalidade", factory.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await factory.DeleteWithCsrfAsync($"/api/admin/leases/{created.Id}",
            new { concurrencyToken = created.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LEASE_HAS_CHARGES", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var verification = factory.Services.CreateAsyncScope();
        var db2 = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await db2.Leases.SingleOrDefaultAsync(x => x.Id == created.Id));
    }

    private CreateLeaseBody Body((Tenant Tenant, Professional Professional, Room Room) value) => new(
        value.Tenant.Id, value.Professional.Id, value.Room.Id, "HOURLY", 150.50m,
        factory.UtcNow.AddDays(-1), 10, factory.UtcNow.AddDays(1),
        factory.UtcNow.AddDays(1).AddHours(2));

    private async Task<(Tenant Tenant, Professional Professional, Room Room)> SeedResourcesAsync()
    {
        var now = factory.UtcNow;
        var tenant = Tenant.Create("Locatário Teste", TenantKind.Individual, now);
        var professional = Professional.Create("Profissional Teste", "Teste", "65999990002", now);
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 100m, 500m, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(tenant, professional, room);
        await db.SaveChangesAsync();
        return (tenant, professional, room);
    }

    private async Task LoginAsync(string role)
    {
        var email = $"lease-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record LeasePage(IReadOnlyList<LeasePayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record LeasePayload(Guid Id, string Status, string ConcurrencyToken);
    private sealed record LeaseDetailPayload(Guid Id, string Status, string ConcurrencyToken,
        DateTimeOffset BillingStartAt, DateTimeOffset OccupancyStartAt, DateTimeOffset? OccupancyEndAt);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record CreateLeaseBody(Guid TenantId, Guid ProfessionalId, Guid RoomId, string Mode,
        decimal ContractedRate, DateTimeOffset BillingStartAt, int? BillingDueDay,
        DateTimeOffset OccupancyStartAt, DateTimeOffset? OccupancyEndAt);
}
