using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

// Task 13: the existing POST /api/admin/leases handler doubles as the conversion route for a
// RoomRentalInquiry (Task 8/12). No parallel endpoint, no duplicated validation — these tests drive
// the same Create handler LeaseAdministrationTests.cs already covers, adding the optional
// RoomRentalInquiryId and asserting the transaction either commits both sides or neither.
[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomRentalInquiryConversionTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private static int _phoneSeed;

    [Fact]
    public async Task New_inquiry_converts_atomically_with_the_lease_and_keeps_its_original_room_id()
    {
        await factory.ResetAsync();
        var originalRoom = await SeedRoomAsync("Sala Original Interesse");
        var inquiry = await SeedInquiryAsync(originalRoom.Id);
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources, inquiry.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.Equal("AGENDADA", created.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.LeaseOccurrences.Where(x => x.LeaseId == created.Id).ToListAsync());
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED" && x.TargetEntityId == created.Id));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_RENTAL_INQUIRY_CONVERTED" && x.TargetEntityId == inquiry.Id));

        var persisted = await db.RoomRentalInquiries.SingleAsync(x => x.Id == inquiry.Id);
        Assert.Equal(RoomRentalInquiryStatus.Converted, persisted.Status);
        Assert.Equal(created.Id, persisted.LeaseId);
        Assert.NotNull(persisted.ConvertedAt);
        // The Admin picked a different room for the contract than the one originally requested by the inquiry:
        // the inquiry's own RoomId must never be overwritten with the Lease's RoomId.
        Assert.NotEqual(originalRoom.Id, resources.Room.Id);
        Assert.Equal(originalRoom.Id, persisted.RoomId);
    }

    [Fact]
    public async Task Creation_without_room_rental_inquiry_id_remains_unaffected()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.Equal("AGENDADA", created.Status);
        Assert.NotEmpty(created.ConcurrencyToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_RENTAL_INQUIRY_CONVERTED"));
    }

    [Fact]
    public async Task Nonexistent_inquiry_id_returns_not_found_and_creates_nothing()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Leases.CountAsync());
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
    }

    [Fact]
    public async Task Already_converted_inquiry_returns_409_and_creates_nothing()
    {
        await factory.ResetAsync();
        var originalRoom = await SeedRoomAsync("Sala Já Convertida");
        var inquiry = await SeedInquiryAsync(originalRoom.Id);
        var previousLeaseId = await SeedLeaseAsync(originalRoom.Id);
        await ConvertInquiryAsync(inquiry.Id, previousLeaseId, factory.UtcNow);
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources, inquiry.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ROOM_RENTAL_INQUIRY_ALREADY_CONVERTED", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Leases.CountAsync(x => x.RoomId == resources.Room.Id));
    }

    [Fact]
    public async Task Resource_conflict_rolls_back_the_conversion_and_leaves_the_inquiry_new()
    {
        await factory.ResetAsync();
        var originalRoom = await SeedRoomAsync("Sala Conflito Interesse");
        var inquiry = await SeedInquiryAsync(originalRoom.Id);
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        // Occupy the target room/professional first so the conversion attempt itself hits LEASE_RESOURCE_CONFLICT.
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources, null))).EnsureSuccessStatusCode();

        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources, inquiry.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LEASE_RESOURCE_CONFLICT", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.RoomRentalInquiries.SingleAsync(x => x.Id == inquiry.Id);
        Assert.Equal(RoomRentalInquiryStatus.New, persisted.Status);
        Assert.Null(persisted.LeaseId);
        Assert.Null(persisted.ConvertedAt);
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_RENTAL_INQUIRY_CONVERTED"));
    }

    [Fact]
    public async Task Concurrent_conversions_of_the_same_inquiry_leave_exactly_one_lease_and_one_conflict()
    {
        await factory.ResetAsync();
        var originalRoom = await SeedRoomAsync("Sala Disputada");
        var inquiry = await SeedInquiryAsync(originalRoom.Id);
        // Deliberately distinct tenant/professional/room per attempt so neither the app-level resource
        // lock nor LEASE_RESOURCE_CONFLICT can serialize/reject either request for an unrelated reason —
        // the only thing that may reject the loser is the conditional inquiry-conversion update.
        var first = await SeedResourcesAsync();
        var second = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);

        var firstAttempt = factory.PostWithCsrfAsync("/api/admin/leases", Body(first, inquiry.Id));
        var secondAttempt = factory.PostWithCsrfAsync("/api/admin/leases", Body(second, inquiry.Id));
        var responses = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var conflictResponse = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("ROOM_RENTAL_INQUIRY_ALREADY_CONVERTED",
            (await conflictResponse.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Leases.CountAsync());
        var winningLease = await db.Leases.SingleAsync();
        var persisted = await db.RoomRentalInquiries.SingleAsync(x => x.Id == inquiry.Id);
        Assert.Equal(RoomRentalInquiryStatus.Converted, persisted.Status);
        Assert.Equal(winningLease.Id, persisted.LeaseId);
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_RENTAL_INQUIRY_CONVERTED"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
    }

    private CreateLeaseBody Body((Tenant Tenant, Professional Professional, Room Room) value, Guid? inquiryId) => new(
        value.Tenant.Id, value.Professional.Id, value.Room.Id, "HOURLY", 150.50m,
        factory.UtcNow.AddDays(-1), 10, factory.UtcNow.AddDays(1),
        factory.UtcNow.AddDays(1).AddHours(2), inquiryId);

    private async Task<Room> SeedRoomAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = Room.Create(name, null, 100m, 500m, factory.UtcNow);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    private async Task<RoomRentalInquiry> SeedInquiryAsync(Guid roomId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inquiry = RoomRentalInquiry.Create(roomId, "Ana Souza", "+5569999999999", "Clínica A", null,
            PublicRoomAvailabilityStatus.AvailableNow, null, factory.UtcNow,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));
        db.RoomRentalInquiries.Add(inquiry);
        await db.SaveChangesAsync();
        return inquiry;
    }

    private async Task<Guid> SeedLeaseAsync(Guid roomId)
    {
        var now = factory.UtcNow;
        var tenant = Tenant.Create($"Locatário Anterior {Guid.NewGuid():N}", TenantKind.Individual, now);
        var professional = Professional.Create($"Profissional Anterior {Guid.NewGuid():N}", "Teste", NextPhone(), now);
        var lease = GestaoPredio.Domain.Leases.Lease.Create(tenant.Id, professional.Id, roomId,
            GestaoPredio.Domain.Leases.LeaseMode.Monthly, 100m, now.AddDays(-30), 10, now.AddDays(-30), null, 10, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(tenant, professional, lease);
        await db.SaveChangesAsync();
        return lease.Id;
    }

    private async Task ConvertInquiryAsync(Guid inquiryId, Guid leaseId, DateTimeOffset occurredAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inquiry = await db.RoomRentalInquiries.SingleAsync(x => x.Id == inquiryId);
        inquiry.Convert(leaseId, occurredAt);
        await db.SaveChangesAsync();
    }

    private async Task<(Tenant Tenant, Professional Professional, Room Room)> SeedResourcesAsync()
    {
        var now = factory.UtcNow;
        var tenant = Tenant.Create($"Locatário Teste {Guid.NewGuid():N}", TenantKind.Individual, now);
        var professional = Professional.Create($"Profissional Teste {Guid.NewGuid():N}", "Teste", NextPhone(), now);
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 100m, 500m, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(tenant, professional, room);
        await db.SaveChangesAsync();
        return (tenant, professional, room);
    }

    private static string NextPhone() => $"6599999{Interlocked.Increment(ref _phoneSeed):D4}";

    private async Task LoginAsync(string role)
    {
        var email = $"lease-inquiry-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record LeasePayload(Guid Id, string Status, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record CreateLeaseBody(Guid TenantId, Guid ProfessionalId, Guid RoomId, string Mode,
        decimal ContractedRate, DateTimeOffset BillingStartAt, int? BillingDueDay,
        DateTimeOffset OccupancyStartAt, DateTimeOffset? OccupancyEndAt, Guid? RoomRentalInquiryId);
}
