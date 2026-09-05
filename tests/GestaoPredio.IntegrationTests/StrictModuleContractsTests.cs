using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

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
