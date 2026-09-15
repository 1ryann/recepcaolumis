using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomRentalInquiryTests(ModulesApiFactory factory)
{
    // Documents the deliberate choice of 200 OK (not 201 Created): the response is a continuation of a
    // synchronous flow (the caller immediately opens WhatsApp with the returned URL), not a resource the
    // client will subsequently fetch by location; InquiryId already identifies the persisted record.
    [Fact]
    public async Task Valid_inquiry_when_room_is_available_now_returns_200_persists_the_snapshot_and_builds_the_whatsapp_message()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala 101");

        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RoomRentalInquiryResultPayload>())!;
        Assert.Equal("Disponível agora", result.PresentedAvailabilityLabel);

        var expected = "Olá! Tenho interesse em alugar uma sala na Lumis.\n\n" +
            "Sala: Sala 101\nDisponibilidade: Disponível agora\nNome: Ana Souza\n" +
            "WhatsApp: +5569999999999\nProfissão/Empresa: Clínica A\nObservação: —";
        Assert.Equal(expected, Uri.UnescapeDataString(new Uri(result.WhatsappUrl).Query[6..]));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inquiry = await db.RoomRentalInquiries.AsNoTracking().SingleAsync(x => x.Id == result.InquiryId);
        Assert.Equal(room.Id, inquiry.RoomId);
        Assert.Equal("Ana Souza", inquiry.FullName);
        Assert.Equal("+5569999999999", inquiry.WhatsApp);
        Assert.Equal("Clínica A", inquiry.ProfessionOrCompany);
        Assert.Null(inquiry.Note);
        Assert.Equal(PublicRoomAvailabilityStatus.AvailableNow, inquiry.PresentedAvailabilityStatus);
        Assert.Null(inquiry.PresentedAvailableFrom);
        Assert.Equal(RoomRentalInquiryStatus.New, inquiry.Status);
        Assert.Null(inquiry.LeaseId);
        Assert.Null(inquiry.ConvertedAt);
        Assert.Equal(new DateOnly(2026, 12, 1), inquiry.DesiredStartDate);
        Assert.Equal(new DateOnly(2026, 12, 10), inquiry.DesiredEndDate);

        var audit = await db.AuditEntries.AsNoTracking()
            .SingleAsync(x => x.Action == AuditActions.RoomRentalInquiryCreated && x.TargetEntityId == inquiry.Id);
        Assert.Equal("SUCCEEDED", audit.Result);
    }

    [Fact]
    public async Task Valid_inquiry_when_room_is_available_soon_persists_the_computed_available_from_date()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala 202");
        await SeedLeaseAsync(room.Id, factory.UtcNow.AddDays(-1), factory.UtcNow.AddDays(1));

        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RoomRentalInquiryResultPayload>())!;
        Assert.Equal("Disponível em breve — a partir de 16/11/2026", result.PresentedAvailabilityLabel);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inquiry = await db.RoomRentalInquiries.AsNoTracking().SingleAsync(x => x.Id == result.InquiryId);
        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, inquiry.PresentedAvailabilityStatus);
        Assert.Equal(new DateOnly(2026, 11, 16), inquiry.PresentedAvailableFrom);
    }

    public static IEnumerable<object?[]> InvalidBodies()
    {
        yield return new object?[] { null, "+5569999999999", "Clínica A", null };
        yield return new object?[] { new string('A', 201), "+5569999999999", "Clínica A", null };
        yield return new object?[] { "Ana Souza", "123", "Clínica A", null };
        yield return new object?[] { "Ana Souza", null, "Clínica A", null };
        yield return new object?[] { "Ana Souza", "+5569999999999", null, null };
        yield return new object?[] { "Ana Souza", "+5569999999999", new string('B', 201), null };
        yield return new object?[] { "Ana Souza", "+5569999999999", "Clínica A", new string('C', 501) };
        yield return new object?[] { "Ana Souza", "+5569999999999", "Clínica A", "<script>alert(1)</script>" };
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_body_returns_400_and_persists_nothing(
        string? fullName, string? whatsApp, string? professionOrCompany, string? note)
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Inválida");

        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName, whatsApp, professionOrCompany, note, desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_ROOM_RENTAL_INQUIRY", await response.Content.ReadAsStringAsync());
        await AssertNoRowsPersistedAsync();
    }

    public static IEnumerable<object?[]> InvalidDesiredDateBodies()
    {
        yield return new object?[] { null, "2026-12-10" };
        yield return new object?[] { "2026-12-01", null };
        yield return new object?[] { "2026-12-10", "2026-12-01" };
    }

    [Theory]
    [MemberData(nameof(InvalidDesiredDateBodies))]
    public async Task Invalid_desired_date_range_returns_400_and_persists_nothing(string? desiredStartDate, string? desiredEndDate)
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Datas Inválidas");

        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new
            {
                fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate, desiredEndDate
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_ROOM_RENTAL_INQUIRY", await response.Content.ReadAsStringAsync());
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Desired_end_date_equal_to_start_date_is_accepted()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Datas Iguais");

        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new
            {
                fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-05", desiredEndDate = "2026-12-05"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Nonexistent_room_returns_404_and_persists_nothing()
    {
        await factory.ResetAsync();

        var response = await PostValidAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Inactive_room_returns_404_and_persists_nothing()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Inativa", deactivate: true);

        var response = await PostValidAsync(room.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Occupied_room_returns_404_and_persists_nothing()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Ocupada");
        await SeedLeaseAsync(room.Id, factory.UtcNow.AddDays(-1), null);

        var response = await PostValidAsync(room.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Unknown_json_property_is_rejected_with_400()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Estrita");

        var response = await PostRawAsync(room.Id,
            "{\"fullName\":\"Ana Souza\",\"whatsApp\":\"+5569999999999\",\"professionOrCompany\":\"Clínica A\"," +
            "\"unexpectedField\":true}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Client_supplied_room_id_or_availability_is_rejected_by_the_strict_body()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Estrita 2");

        var response = await PostRawAsync(room.Id,
            "{\"fullName\":\"Ana Souza\",\"whatsApp\":\"+5569999999999\",\"professionOrCompany\":\"Clínica A\"," +
            "\"roomId\":\"" + Guid.NewGuid() + "\",\"status\":\"AVAILABLE_NOW\"}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Whatsapp_not_configured_returns_503_and_does_not_persist()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Sem Whatsapp");
        using var unconfigured = factory.WithConfig(("Whatsapp:FinanceiroPhoneNumber", ""));

        var response = await unconfigured.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("ROOM_RENTAL_WHATSAPP_NOT_CONFIGURED", await response.Content.ReadAsStringAsync());
        await AssertNoRowsPersistedAsync();
    }

    [Fact]
    public async Task Ip_budget_exhausted_returns_429()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Limite IP");
        using var throttled = factory.WithConfig(
            ("RateLimiting:RoomRentalInquiryIpPermitLimit", "1"),
            ("RateLimiting:RoomRentalInquiryIdentifierPermitLimit", "10000"));

        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999990001", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" })).StatusCode);
        var response = await throttled.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Bruna Lima", whatsApp = "+5569999990002", professionOrCompany = "Clínica B", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Contains("TOO_MANY_REQUESTS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Whatsapp_identifier_budget_exhausted_returns_429()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Limite Telefone");
        using var throttled = factory.WithConfig(
            ("RateLimiting:RoomRentalInquiryIpPermitLimit", "10000"),
            ("RateLimiting:RoomRentalInquiryIdentifierPermitLimit", "1"));

        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" })).StatusCode);
        var response = await throttled.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Contains("TOO_MANY_REQUESTS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Recalculates_availability_live_at_post_time_instead_of_trusting_any_client_or_cached_state()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = await SeedRoomAsync("Sala Dinâmica");

        var first = await PostValidAsync(room.Id);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = (await first.Content.ReadFromJsonAsync<RoomRentalInquiryResultPayload>())!;
        Assert.Equal("Disponível agora", firstResult.PresentedAvailabilityLabel);

        // A lease created after the first POST must change what the second POST computes: the server
        // never trusts a client-provided or previously-returned availability.
        await SeedLeaseAsync(room.Id, factory.UtcNow.AddDays(-1), factory.UtcNow.AddDays(1));

        var second = await PostValidAsync(room.Id);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondResult = (await second.Content.ReadFromJsonAsync<RoomRentalInquiryResultPayload>())!;
        Assert.Equal("Disponível em breve — a partir de 16/11/2026", secondResult.PresentedAvailabilityLabel);
    }

    [Fact]
    public async Task Endpoint_never_logs_the_request_fields_message_or_whatsapp_url()
    {
        await factory.ResetAsync();
        var room = await SeedRoomAsync("Sala Log");
        var capture = factory.CaptureLogs();

        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{room.Id}/rental-inquiries",
            new { fullName = "Verificação Confidencial", whatsApp = "+5569999999999", professionOrCompany = "Empresa Secreta", note = "Nota sigilosa",
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RoomRentalInquiryResultPayload>())!;
        var logs = capture.Text;
        Assert.DoesNotContain("Verificação Confidencial", logs);
        Assert.DoesNotContain("Empresa Secreta", logs);
        Assert.DoesNotContain("Nota sigilosa", logs);
        Assert.DoesNotContain("5569999999999", logs);
        Assert.DoesNotContain(result.WhatsappUrl, logs);
        Assert.DoesNotContain("Tenho interesse em alugar", logs);
    }

    private async Task<HttpResponseMessage> PostValidAsync(Guid roomId) =>
        await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{roomId}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null,
                desiredStartDate = "2026-12-01", desiredEndDate = "2026-12-10" });

    private async Task<HttpResponseMessage> PostRawAsync(Guid roomId, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/totem/rooms/{roomId}/rental-inquiries")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return await factory.Client.SendAsync(request);
    }

    private async Task AssertNoRowsPersistedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.RoomRentalInquiries.ToListAsync());
        Assert.Empty(await db.AuditEntries.Where(x => x.Action == AuditActions.RoomRentalInquiryCreated).ToListAsync());
    }

    private async Task<Room> SeedRoomAsync(string name, bool deactivate = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = Room.Create(name, null, 100m, 500m, factory.UtcNow);
        if (deactivate) room.Deactivate(factory.UtcNow);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    private async Task SeedLeaseAsync(Guid roomId, DateTimeOffset start, DateTimeOffset? end)
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
    }

    private sealed record RoomRentalInquiryResultPayload(Guid InquiryId, string WhatsappUrl, string PresentedAvailabilityLabel);
}
