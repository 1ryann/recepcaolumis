using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
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
