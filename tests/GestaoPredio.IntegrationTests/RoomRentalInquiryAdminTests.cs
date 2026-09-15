using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomRentalInquiryAdminTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Admin_queries_require_operations_policy()
    {
        await factory.ResetAsync();
        var response = await factory.Client.GetAsync("/api/admin/room-rental-inquiries");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var detailResponse = await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "inquiry-admin-prof@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/admin/room-rental-inquiries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Administrador)]
    [InlineData(SystemRoles.Gerente)]
    public async Task Operations_roles_receive_the_default_page(string role)
    {
        await factory.ResetAsync();
        await LoginAsAsync(role, $"inquiry-admin-{role.ToLowerInvariant()}@lumis.test");
        var response = await factory.Client.GetAsync("/api/admin/room-rental-inquiries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<InquiryPage>())!;
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(SystemRoles.Administrador)]
    [InlineData(SystemRoles.Gerente)]
    public async Task Operations_roles_receive_200_on_the_detail_route(string role)
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Detalhe Papéis");
        var inquiry = await SeedInquiryAsync(room.Id, "Ana Souza", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableNow, null);
        await LoginAsAsync(role, $"inquiry-admin-detail-{role.ToLowerInvariant()}@lumis.test");

        var response = await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{inquiry.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("page=0", "INVALID_PAGE")]
    [InlineData("pageSize=101", "INVALID_PAGE_SIZE")]
    public async Task Invalid_paging_uses_the_stable_error_shape(string query, string code)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"inquiry-admin-invalid-{Guid.NewGuid():N}@lumis.test");
        var response = await factory.Client.GetAsync($"/api/admin/room-rental-inquiries?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task List_orders_by_created_at_desc_then_id_desc_and_joins_the_room_name()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala Original");
        // Same CreatedAt instant, two different inquiries: only the Id desc tiebreaker can order them deterministically.
        var older = await SeedInquiryAsync(room.Id, "Ana Souza", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableNow, null);
        var newer = await SeedInquiryAsync(room.Id, "Bruna Lima", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableNow, null);
        var expectedOrder = new[] { older.Id, newer.Id }.OrderByDescending(id => id).ToArray();

        await LoginAsAsync(SystemRoles.Administrador, "inquiry-admin-order@lumis.test");
        var page = (await (await factory.Client.GetAsync("/api/admin/room-rental-inquiries"))
            .Content.ReadFromJsonAsync<InquiryPage>())!;

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(expectedOrder, page.Items.Select(x => x.Id));
        Assert.All(page.Items, item => Assert.Equal("Sala Original", item.RoomName));

        // Renaming the room afterwards must change what the join returns: RoomName is never a denormalized copy.
        await RenameRoomAsync(room.Id, "Sala Renomeada");
        var renamed = (await (await factory.Client.GetAsync("/api/admin/room-rental-inquiries"))
            .Content.ReadFromJsonAsync<InquiryPage>())!;
        Assert.All(renamed.Items, item => Assert.Equal("Sala Renomeada", item.RoomName));
    }

    [Fact]
    public async Task Label_reflects_the_historical_snapshot_even_after_the_room_leases_change()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala Snapshot");
        var inquiry = await SeedInquiryAsync(room.Id, "Ana Souza", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableNow, null);

        // A lease created after the inquiry must NOT change what the admin endpoints report: the label/
        // status/date are a persisted snapshot at inquiry-creation time, not a live recomputation.
        await SeedLeaseAsync(room.Id, factory.UtcNow.AddDays(-1), factory.UtcNow.AddDays(1));

        await LoginAsAsync(SystemRoles.Administrador, "inquiry-admin-snapshot@lumis.test");
        var page = (await (await factory.Client.GetAsync("/api/admin/room-rental-inquiries"))
            .Content.ReadFromJsonAsync<InquiryPage>())!;
        var listed = Assert.Single(page.Items);
        Assert.Equal("Disponível agora", listed.PresentedAvailabilityLabel);
        Assert.Equal("AVAILABLE_NOW", listed.PresentedAvailabilityStatus);
        Assert.Null(listed.PresentedAvailableFrom);

        var detail = (await (await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{inquiry.Id}"))
            .Content.ReadFromJsonAsync<InquiryDetail>())!;
        Assert.Equal("Disponível agora", detail.PresentedAvailabilityLabel);
        Assert.Equal("AVAILABLE_NOW", detail.PresentedAvailabilityStatus);
        Assert.Null(detail.PresentedAvailableFrom);
    }

    [Fact]
    public async Task Available_soon_snapshot_formats_the_persisted_date_not_a_recomputed_one()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala Em Breve");
        await SeedInquiryAsync(room.Id, "Ana Souza", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableSoon,
            new DateOnly(2026, 11, 16));

        await LoginAsAsync(SystemRoles.Administrador, "inquiry-admin-soon@lumis.test");
        var page = (await (await factory.Client.GetAsync("/api/admin/room-rental-inquiries"))
            .Content.ReadFromJsonAsync<InquiryPage>())!;
        var listed = Assert.Single(page.Items);
        Assert.Equal("Disponível em breve — a partir de 16/11/2026", listed.PresentedAvailabilityLabel);
        Assert.Equal("AVAILABLE_SOON", listed.PresentedAvailabilityStatus);
        Assert.Equal(new DateOnly(2026, 11, 16), listed.PresentedAvailableFrom);
    }

    [Fact]
    public async Task Detail_reports_new_and_converted_states_including_lease_id_and_converted_at()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala Conversão");
        var newInquiry = await SeedInquiryAsync(room.Id, "Ana Souza", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableNow, null);
        var convertedInquiry = await SeedInquiryAsync(room.Id, "Bruna Lima", factory.UtcNow, PublicRoomAvailabilityStatus.AvailableNow, null);
        var leaseId = await SeedLeaseAsync(room.Id, factory.UtcNow, null);
        var convertedAt = factory.UtcNow.AddHours(1);
        await ConvertInquiryAsync(convertedInquiry.Id, leaseId, convertedAt);

        await LoginAsAsync(SystemRoles.Administrador, "inquiry-admin-conversion@lumis.test");

        var newDetail = (await (await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{newInquiry.Id}"))
            .Content.ReadFromJsonAsync<InquiryDetail>())!;
        Assert.Equal("NEW", newDetail.Status);
        Assert.Null(newDetail.LeaseId);
        Assert.Null(newDetail.ConvertedAt);

        var convertedDetail = (await (await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{convertedInquiry.Id}"))
            .Content.ReadFromJsonAsync<InquiryDetail>())!;
        Assert.Equal("CONVERTED", convertedDetail.Status);
        Assert.Equal(leaseId, convertedDetail.LeaseId);
        Assert.NotNull(convertedDetail.ConvertedAt);
    }

    [Fact]
    public async Task Detail_of_a_nonexistent_inquiry_is_not_found()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "inquiry-admin-missing@lumis.test");
        var response = await factory.Client.GetAsync($"/api/admin/room-rental-inquiries/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task No_status_mutation_route_exists_for_inquiries(string method)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"inquiry-admin-nomutation-{method.ToLowerInvariant()}@lumis.test");
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/admin/room-rental-inquiries/{Guid.NewGuid()}");
        var response = await factory.Client.SendAsync(request);
        // No such route is mapped, so the request falls through to the authenticated
        // `/api/{**path}` catch-all in Program.cs, which reports 404 for a signed-in caller.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Room> SeedRoomAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = Room.Create(name, null, 100m, 500m, factory.UtcNow);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    private async Task RenameRoomAsync(Guid roomId, string newName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = await db.Rooms.SingleAsync(x => x.Id == roomId);
        room.Update(newName, room.Description, room.HourlyRate, room.DailyRate, factory.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<RoomRentalInquiry> SeedInquiryAsync(Guid roomId, string fullName, DateTimeOffset occurredAt,
        PublicRoomAvailabilityStatus status, DateOnly? availableFrom)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inquiry = RoomRentalInquiry.Create(roomId, fullName, "+5569999999999", "Clínica A", null,
            status, availableFrom, occurredAt);
        db.RoomRentalInquiries.Add(inquiry);
        await db.SaveChangesAsync();
        return inquiry;
    }

    private async Task ConvertInquiryAsync(Guid inquiryId, Guid leaseId, DateTimeOffset occurredAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inquiry = await db.RoomRentalInquiries.SingleAsync(x => x.Id == inquiryId);
        inquiry.Convert(leaseId, occurredAt);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedLeaseAsync(Guid roomId, DateTimeOffset start, DateTimeOffset? end)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.Create("Locatário Interesse", TenantKind.Individual, factory.UtcNow);
        var professional = Professional.Create("Profissional Interesse", "Teste", "65999990004", factory.UtcNow);
        db.AddRange(tenant, professional);
        var lease = Lease.Create(tenant.Id, professional.Id, roomId, LeaseMode.Monthly, 100m,
            start.AddDays(-1), 10, start, end, 10, factory.UtcNow);
        db.Leases.Add(lease);
        await db.SaveChangesAsync();
        return lease.Id;
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record InquiryPage(IReadOnlyList<InquiryItem> Items, int Page, int PageSize, int TotalCount);

    private sealed record InquiryItem(
        Guid Id, Guid RoomId, string RoomName, string FullName, string WhatsApp, string ProfessionOrCompany,
        string? Note, string PresentedAvailabilityStatus, DateOnly? PresentedAvailableFrom,
        string PresentedAvailabilityLabel, string Status, Guid? LeaseId, DateTimeOffset? ConvertedAt, DateTimeOffset CreatedAt);

    private sealed record InquiryDetail(
        Guid Id, Guid RoomId, string RoomName, string FullName, string WhatsApp, string ProfessionOrCompany,
        string? Note, string PresentedAvailabilityStatus, DateOnly? PresentedAvailableFrom,
        string PresentedAvailabilityLabel, string Status, Guid? LeaseId, DateTimeOffset? ConvertedAt, DateTimeOffset CreatedAt);

    private sealed record ErrorPayload(string Code, string Message);
}
