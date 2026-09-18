using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Whatsapp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class WhatsappApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private const string TestPath = "/api/admin/whatsapp/test";
    private const string WebhookPath = "/api/whatsapp/webhook";
    private const string Token = "integration-access-token-not-real-abcdef";
    private const string VerifyToken = "integration-verify-token-not-real";
    private const string AppSecret = "integration-app-secret-not-real";

    [Fact]
    public async Task Anonymous_and_non_admin_users_cannot_send_a_test_message()
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler();
        using var host = CreateHost(graph, ConfiguredSettings());

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await host.Client.PostAsJsonAsync(TestPath, new { phoneNumber = "+5569999999999", message = "Olá" })).StatusCode);

        foreach (var role in new[] { SystemRoles.Gerente, SystemRoles.Profissional, SystemRoles.Customer })
        {
            var user = await factory.CreateUserAsync($"wa-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@lumis.test", Password, [role]);
            Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(user.Email!, Password)).StatusCode);
            var response = await host.PostWithCsrfAsync(TestPath, new { phoneNumber = "+5569999999999", message = "Olá" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Admin_request_without_csrf_token_is_rejected_before_any_send()
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler();
        using var host = CreateHost(graph, ConfiguredSettings());
        var admin = await factory.CreateUserAsync($"wa-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await host.Client.PostAsJsonAsync(TestPath, new { phoneNumber = "+5569999999999", message = "Olá" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Admin_sends_a_test_message_audited_without_phone_body_or_token()
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler();
        using var host = CreateHost(graph, ConfiguredSettings());
        var admin = await factory.CreateUserAsync($"wa-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(admin.Email!, Password)).StatusCode);
        var logs = factory.CaptureLogs();

        var response = await host.PostWithCsrfAsync(TestPath, new { phoneNumber = "(69) 98888-7777", message = "Mensagem de teste LUMIS" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var payload = (await response.Content.ReadFromJsonAsync<TestPayload>())!;
        Assert.True(payload.Success);
        Assert.Equal("wamid.INTEGRATION", payload.MessageId);
        Assert.Null(payload.FailureCode);
        var sent = Assert.Single(graph.Requests);
        Assert.Equal("https://graph.test.invalid/v99.0/1004068049466823/messages", sent.Uri);
        Assert.Equal($"Bearer {Token}", sent.Authorization);
        Assert.Contains("\"to\":\"5569988887777\"", sent.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, raw, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditEntries.Where(x => x.Action.StartsWith("WHATSAPP_TEST_")).ToListAsync();
        Assert.Contains(audits, x => x.Action == "WHATSAPP_TEST_SUCCEEDED" && x.Result == "SUCCEEDED" && x.ActorUserId == admin.Id);
        var auditText = string.Join('|', audits.Select(x => $"{x.CorrelationId}{x.TargetEntityType}{x.ChangedFields}"));
        Assert.DoesNotContain("988887777", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("Mensagem de teste", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("988887777", logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mensagem de teste", logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_access_token_returns_service_unavailable_without_calling_meta()
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler();
        var settings = ConfiguredSettings().Where(x => x.Key != "Whatsapp:AccessToken")
            .Append(("Whatsapp:AccessToken", "")).ToArray();
        using var host = CreateHost(graph, settings);
        var admin = await factory.CreateUserAsync($"wa-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await host.PostWithCsrfAsync(TestPath, new { phoneNumber = "+5569999999999", message = "Olá" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(WhatsAppFailureCodes.NotConfigured, (await response.Content.ReadFromJsonAsync<TestPayload>())!.FailureCode);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Provider_rejection_is_reported_as_bad_gateway_with_a_stable_code()
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler
        {
            Status = HttpStatusCode.BadRequest,
            ResponseBody = $$$"""{"error":{"message":"Recipient phone number not in allowed list {{{Token}}}","code":131030,"fbtrace_id":"ABC"}}"""
        };
        using var host = CreateHost(graph, ConfiguredSettings());
        var admin = await factory.CreateUserAsync($"wa-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await host.PostWithCsrfAsync(TestPath, new { phoneNumber = "+5569999999999", message = "Olá" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains(WhatsAppFailureCodes.RecipientNotAllowed, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("allowed list", raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123", "Olá")]
    [InlineData("+5569999999999", "")]
    public async Task Invalid_test_payload_is_rejected_without_calling_meta(string phone, string message)
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler();
        using var host = CreateHost(graph, ConfiguredSettings());
        var admin = await factory.CreateUserAsync($"wa-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await host.PostWithCsrfAsync(TestPath, new { phoneNumber = phone, message });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Test_payload_does_not_accept_a_token_or_other_unknown_fields()
    {
        await factory.ResetAsync();
        var graph = new RecordingGraphHandler();
        using var host = CreateHost(graph, ConfiguredSettings());
        var admin = await factory.CreateUserAsync($"wa-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await host.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await host.PostWithCsrfAsync(TestPath,
            new { phoneNumber = "+5569999999999", message = "Olá", accessToken = "attacker-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Webhook_verification_echoes_the_challenge_only_for_the_configured_verify_token()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());

        var ok = await host.Client.GetAsync($"{WebhookPath}?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=1158201444");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("1158201444", await ok.Content.ReadAsStringAsync());
        Assert.StartsWith("text/plain", ok.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);

        foreach (var query in new[]
                 {
                     "hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1",
                     $"hub.mode=unsubscribe&hub.verify_token={VerifyToken}&hub.challenge=1",
                     $"hub.mode=subscribe&hub.verify_token={VerifyToken}",
                     "hub.mode=subscribe&hub.challenge=1"
                 })
        {
            var rejected = await host.Client.GetAsync($"{WebhookPath}?{query}");
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            Assert.DoesNotContain(VerifyToken, await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Webhook_verification_is_rejected_when_no_verify_token_is_configured()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), [("Whatsapp:VerifyToken", "")]);

        var response = await host.Client.GetAsync($"{WebhookPath}?hub.mode=subscribe&hub.verify_token=&hub.challenge=1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_event_with_missing_or_invalid_signature_is_rejected()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";

        var unsigned = await host.Client.PostAsync(WebhookPath, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        wrong.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, "some-other-secret"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(wrong)).StatusCode);
    }

    [Fact]
    public async Task Webhook_event_is_rejected_when_no_app_secret_is_configured()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), [("Whatsapp:AppSecret", "")]);
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, AppSecret));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Signed_webhook_event_is_acknowledged_anonymously_without_csrf_and_without_logging_content()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());
        var logs = factory.CaptureLogs();
        const string body = """
            {"object":"whatsapp_business_account","entry":[{"id":"123","changes":[{"field":"messages","value":{
              "messaging_product":"whatsapp","metadata":{"display_phone_number":"5511993828941","phone_number_id":"1004068049466823"},
              "contacts":[{"profile":{"name":"Cliente Sigiloso"},"wa_id":"5569977776666"}],
              "messages":[{"from":"5569977776666","id":"wamid.IN","timestamp":"1","type":"text","text":{"body":"conteudo privado do cliente"}}],
              "statuses":[{"id":"wamid.OUT","status":"delivered","timestamp":"1","recipient_id":"5569977776666"}]}}]}]}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, AppSecret));

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("977776666", logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("conteudo privado", logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cliente Sigiloso", logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(AppSecret, logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signed_status_events_are_processed_for_sent_delivered_read_and_failed()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());
        var recorder = host.Services.GetRequiredService<WhatsAppStatusRecorder>();
        recorder.Clear();
        var logs = factory.CaptureLogs();

        foreach (var (status, extra) in new[]
                 {
                     ("sent", ""),
                     ("delivered", ""),
                     ("read", ""),
                     ("failed", ""","errors":[{"code":131026,"title":"Message undeliverable","error_data":{"details":"Receiver is incapable of receiving this message"}}]""")
                 })
        {
            var body = StatusPayload(status, extra);
            using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, AppSecret));
            Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(request)).StatusCode);
        }

        var recorded = recorder.ForMessage(SentMessageId);
        Assert.Equal([WhatsAppDeliveryStatus.Sent, WhatsAppDeliveryStatus.Delivered, WhatsAppDeliveryStatus.Read, WhatsAppDeliveryStatus.Failed],
            recorded.Select(x => x.Status));
        Assert.All(recorded, x =>
        {
            Assert.Equal("1004060849466823", x.PhoneNumberId);
            Assert.Equal("1500039464855591", x.WhatsAppBusinessAccountId);
            Assert.Equal("5569999538007", x.RecipientId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789670000), x.OccurredAt);
        });
        var failed = recorded[^1];
        Assert.Equal(131026, failed.ErrorCode);
        Assert.Equal("Message undeliverable", failed.ErrorTitle);

        var text = logs.Text;
        Assert.Contains("delivered", text, StringComparison.Ordinal);
        Assert.Contains("131026", text, StringComparison.Ordinal);
        Assert.Contains("***8007", text, StringComparison.Ordinal);
        Assert.DoesNotContain("5569999538007", text, StringComparison.Ordinal);
        Assert.DoesNotContain(AppSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_webhook_delivery_of_the_same_status_is_acknowledged_but_recorded_once()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());
        var recorder = host.Services.GetRequiredService<WhatsAppStatusRecorder>();
        recorder.Clear();
        var body = StatusPayload("delivered", "");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, AppSecret));
            Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(request)).StatusCode);
        }

        Assert.Single(recorder.ForMessage(SentMessageId));
    }

    [Fact]
    public async Task Inbound_message_events_are_acknowledged_without_recording_a_status()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());
        var recorder = host.Services.GetRequiredService<WhatsAppStatusRecorder>();
        recorder.Clear();
        const string body = """
            {"object":"whatsapp_business_account","entry":[{"id":"1500039464855591","changes":[{"field":"messages","value":{
              "metadata":{"phone_number_id":"1004060849466823"},
              "messages":[{"from":"5569977776666","id":"wamid.IN","timestamp":"1","type":"text","text":{"body":"conteudo privado"}}]}}]}]}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, AppSecret));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(request)).StatusCode);
        Assert.Empty(recorder.Recent);
    }

    [Fact]
    public async Task Signed_but_malformed_webhook_body_is_rejected_as_bad_request()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());
        const string body = "not json";
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(WhatsAppWebhookSecurity.SignatureHeader, Sign(body, AppSecret));

        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Existing_financeiro_phone_configuration_is_preserved_alongside_the_cloud_api_settings()
    {
        await factory.ResetAsync();
        using var host = CreateHost(new RecordingGraphHandler(), ConfiguredSettings());

        var cloud = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WhatsAppCloudOptions>>().Value;
        var financeiro = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<recepcaototem.Features.Rooms.WhatsappOptions>>().Value;

        Assert.True(cloud.IsSendConfigured);
        Assert.Equal("+5569999999999", financeiro.FinanceiroPhoneNumber);
    }

    private static (string Key, string Value)[] ConfiguredSettings() =>
    [
        ("Whatsapp:PhoneNumberId", "1004068049466823"),
        ("Whatsapp:AccessToken", Token),
        ("Whatsapp:ApiVersion", "v99.0"),
        ("Whatsapp:BaseUrl", "https://graph.test.invalid"),
        ("Whatsapp:VerifyToken", VerifyToken),
        ("Whatsapp:AppSecret", AppSecret)
    ];

    // The wamid of the real first send, so the same shape Meta will deliver is exercised here.
    private const string SentMessageId = "wamid.HBgMNTU2OTk5NTM4MDA3FQIAERgSMUZFRkQwNDFEMkE5QzA4NUEwAA==";

    private static string StatusPayload(string status, string extraFields) =>
        $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"1500039464855591","changes":[{"field":"statuses","value":{
          "messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"+55 11 99382-8941","phone_number_id":"1004060849466823"},
          "statuses":[{"id":"{{{SentMessageId}}}","status":"{{{status}}}","timestamp":"1789670000","recipient_id":"5569999538007"{{{extraFields}}}}]}}]}]}
        """;

    private static string Sign(string body, string secret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private WhatsappHost CreateHost(RecordingGraphHandler graph, (string Key, string Value)[] settings)
    {
        var derived = factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>()
                    .ConfigurePrimaryHttpMessageHandler(() => graph));
        });
        var client = derived.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        return new WhatsappHost(derived, client);
    }

    private sealed class WhatsappHost(WebApplicationFactory<recepcaototem.Pages.IndexModel> derived, HttpClient client) : IDisposable
    {
        public HttpClient Client => client;
        public IServiceProvider Services => derived.Services;

        public async Task<HttpResponseMessage> LoginAsync(string email, string password)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password }) };
            request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
            return await client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> PostWithCsrfAsync(string path, object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
            return await client.SendAsync(request);
        }

        private async Task<string> CsrfAsync() =>
            (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<CsrfPayload>())!.Token;

        public void Dispose()
        {
            client.Dispose();
            derived.Dispose();
        }
    }

    private sealed class RecordingGraphHandler : HttpMessageHandler
    {
        public List<(string Uri, string? Authorization, string Body)> Requests { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = """{"messaging_product":"whatsapp","messages":[{"id":"wamid.INTEGRATION"}]}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests) Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record CsrfPayload(string Token);
    private sealed record TestPayload(bool Success, string? MessageId, string? FailureCode);
}
