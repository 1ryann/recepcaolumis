using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Features.Leases;
using recepcaototem.Features.Rooms;

namespace GestaoPredio.IntegrationTests;

public sealed class StrictModuleContractsTests
{
    [Fact]
    public void Module_request_rejects_unknown_json_property()
    {
        var options = new JsonOptions();
        StrictBody.Configure(options);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProbeRequest>(
            "{\"name\":\"ok\",\"isActive\":false}", options.SerializerOptions));
    }

    [Fact]
    public void Room_rental_inquiry_contract_rejects_client_supplied_room_id_and_availability()
    {
        var options = new JsonOptions();
        StrictBody.Configure(options);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RoomRentalInquiryRequest>(
            "{\"fullName\":\"Ana Souza\",\"whatsApp\":\"+5569999999999\",\"professionOrCompany\":\"Clínica A\"," +
            "\"roomId\":\"" + Guid.NewGuid() + "\",\"status\":\"AVAILABLE_NOW\"}", options.SerializerOptions));
    }

    [Fact]
    public void Lease_creation_contract_accepts_only_the_new_optional_room_rental_inquiry_id_member()
    {
        var options = new JsonOptions();
        StrictBody.Configure(options);

        var withInquiry = JsonSerializer.Deserialize<CreateLeaseRequest>(
            "{\"tenantId\":\"" + Guid.NewGuid() + "\",\"professionalId\":\"" + Guid.NewGuid() + "\",\"roomId\":\"" +
            Guid.NewGuid() + "\",\"mode\":\"HOURLY\",\"contractedRate\":100,\"billingStartAt\":\"2026-01-01T00:00:00Z\"," +
            "\"billingDueDay\":10,\"occupancyStartAt\":\"2026-01-02T00:00:00Z\",\"occupancyEndAt\":\"2026-01-02T01:00:00Z\"," +
            "\"roomRentalInquiryId\":\"" + Guid.NewGuid() + "\"}",
            options.SerializerOptions);
        Assert.NotNull(withInquiry);
        Assert.NotNull(withInquiry!.RoomRentalInquiryId);

        var withoutInquiry = JsonSerializer.Deserialize<CreateLeaseRequest>(
            "{\"tenantId\":\"" + Guid.NewGuid() + "\",\"professionalId\":\"" + Guid.NewGuid() + "\",\"roomId\":\"" +
            Guid.NewGuid() + "\",\"mode\":\"HOURLY\",\"contractedRate\":100,\"billingStartAt\":\"2026-01-01T00:00:00Z\"," +
            "\"billingDueDay\":10,\"occupancyStartAt\":\"2026-01-02T00:00:00Z\",\"occupancyEndAt\":\"2026-01-02T01:00:00Z\"}",
            options.SerializerOptions);
        Assert.NotNull(withoutInquiry);
        Assert.Null(withoutInquiry!.RoomRentalInquiryId);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateLeaseRequest>(
            "{\"tenantId\":\"" + Guid.NewGuid() + "\",\"professionalId\":\"" + Guid.NewGuid() + "\",\"roomId\":\"" +
            Guid.NewGuid() + "\",\"mode\":\"HOURLY\",\"contractedRate\":100,\"billingStartAt\":\"2026-01-01T00:00:00Z\"," +
            "\"billingDueDay\":10,\"occupancyStartAt\":\"2026-01-02T00:00:00Z\",\"occupancyEndAt\":\"2026-01-02T01:00:00Z\"," +
            "\"legacyIgnored\":true}",
            options.SerializerOptions));
    }

    [Fact]
    public void Existing_auth_contract_keeps_its_published_json_semantics()
    {
        var options = new JsonOptions();
        StrictBody.Configure(options);

        var request = JsonSerializer.Deserialize<LoginRequest>(
            "{\"email\":\"admin@example.com\",\"password\":\"secret\",\"legacyIgnored\":true}",
            options.SerializerOptions);

        Assert.NotNull(request);
        Assert.Equal("admin@example.com", request.Email);
    }

    private sealed record ProbeRequest(string Name) : IStrictModuleRequest;
}
