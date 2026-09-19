using System.Net;
using System.Text;
using System.Text.Json;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Infrastructure.Whatsapp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppCloudApiServiceTests
{
    private const string Token = "unit-test-access-token-not-real-0123456789";

    [Fact]
    public async Task Sends_a_text_message_to_the_configured_phone_number_id_and_returns_the_message_id()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            """{"messaging_product":"whatsapp","contacts":[{"input":"5569999999999","wa_id":"5569999999999"}],"messages":[{"id":"wamid.TEST123"}]}""");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+55 69 99999-9999", "Olá do LUMIS", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("wamid.TEST123", result.MessageId);
        Assert.Null(result.FailureCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://graph.example.test/v99.0/1004068049466823/messages", request.Uri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(Token, request.AuthorizationParameter);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("whatsapp", body.RootElement.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", body.RootElement.GetProperty("recipient_type").GetString());
        Assert.Equal("5569999999999", body.RootElement.GetProperty("to").GetString());
        Assert.Equal("text", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("Olá do LUMIS", body.RootElement.GetProperty("text").GetProperty("body").GetString());
        Assert.False(body.RootElement.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Theory]
    [InlineData("AccessToken")]
    [InlineData("PhoneNumberId")]
    [InlineData("ApiVersion")]
    [InlineData("BaseUrl")]
    public async Task Missing_send_configuration_fails_closed_without_calling_the_api(string missing)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var options = ConfiguredOptions();
        typeof(WhatsAppCloudOptions).GetProperty(missing)!.SetValue(options, "");
        var service = CreateService(handler, options);

        var result = await service.SendTextAsync("+5569999999999", "Olá", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(WhatsAppFailureCodes.NotConfigured, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("não é telefone")]
    public async Task Invalid_recipient_is_rejected_without_calling_the_api(string phone)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync(phone, "Olá", CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.InvalidRecipient, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_message_is_rejected_without_calling_the_api(string message)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+5569999999999", message, CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.InvalidMessage, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Message_above_the_cloud_api_text_limit_is_rejected_without_calling_the_api()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+5569999999999", new string('a', 4097), CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.InvalidMessage, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 190, WhatsAppFailureCodes.AuthenticationFailed)]
    [InlineData(HttpStatusCode.BadRequest, 131030, WhatsAppFailureCodes.RecipientNotAllowed)]
    [InlineData(HttpStatusCode.BadRequest, 131047, WhatsAppFailureCodes.OutsideServiceWindow)]
    [InlineData(HttpStatusCode.TooManyRequests, 130429, WhatsAppFailureCodes.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, 100, WhatsAppFailureCodes.RequestRejected)]
    [InlineData(HttpStatusCode.InternalServerError, 1, WhatsAppFailureCodes.ProviderUnavailable)]
    public async Task Graph_api_errors_are_mapped_to_stable_failure_codes(HttpStatusCode status, int metaCode, string expected)
    {
        var handler = new FakeHandler(status,
            $$$"""{"error":{"message":"(#{{{metaCode}}}) provider detail with {{{Token}}}","type":"OAuthException","code":{{{metaCode}}},"fbtrace_id":"TRACE1"}}""");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+5569999999999", "Olá", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureCode);
        Assert.Equal(metaCode, result.ProviderErrorCode);
        Assert.DoesNotContain(Token, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_json_error_response_is_still_mapped_by_status()
    {
        var handler = new FakeHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+5569999999999", "Olá", CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.ProviderUnavailable, result.FailureCode);
        Assert.Null(result.ProviderErrorCode);
    }

    [Fact]
    public async Task Success_response_without_a_message_id_is_reported_as_invalid_response()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"messaging_product":"whatsapp"}""");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+5569999999999", "Olá", CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.InvalidResponse, result.FailureCode);
    }

    [Fact]
    public async Task Provider_that_does_not_answer_within_the_configured_timeout_returns_timeout()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}") { Delay = Timeout.InfiniteTimeSpan };
        var options = ConfiguredOptions();
        options.TimeoutSeconds = 1;
        var service = CreateService(handler, options);

        var result = await service.SendTextAsync("+5569999999999", "Olá", CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.Timeout, result.FailureCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_instead_of_being_reported_as_timeout()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}") { Delay = Timeout.InfiniteTimeSpan };
        var service = CreateService(handler, ConfiguredOptions());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendTextAsync("+5569999999999", "Olá", cancellation.Token));
    }

    [Fact]
    public async Task Network_failure_returns_network_error()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}") { Throw = new HttpRequestException("connection refused") };
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTextAsync("+5569999999999", "Olá", CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.NetworkError, result.FailureCode);
    }

    [Fact]
    public async Task Logs_never_contain_the_access_token_the_recipient_or_the_message_body()
    {
        var logs = new ListLoggerProvider();
        foreach (var (status, body) in new[]
                 {
                     (HttpStatusCode.OK, """{"messages":[{"id":"wamid.OK"}]}"""),
                     (HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"bad {{{Token}}}","code":190,"fbtrace_id":"T"}}""")
                 })
        {
            var service = CreateService(new FakeHandler(status, body), ConfiguredOptions(), logs);
            await service.SendTextAsync("+5569988887777", "corpo secreto da mensagem", CancellationToken.None);
        }

        var text = logs.Text;
        Assert.NotEmpty(text);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("988887777", text, StringComparison.Ordinal);
        Assert.DoesNotContain("corpo secreto", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_a_template_with_body_parameters_and_records_it_as_a_template_message()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.TPL1"}]}""");
        var store = new RecordingMessageStore();
        var service = CreateService(handler, ConfiguredOptions(), store: store);

        var result = await service.SendTemplateAsync("+5569999999999",
            new WhatsAppTemplate("professional_client_checked_in", "pt_BR", ["Dra. Ana", "João", "14:30"]),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("wamid.TPL1", result.MessageId);
        Assert.Equal([("wamid.TPL1", Domain.Whatsapp.WhatsAppMessageType.Template)], store.Accepted);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var root = body.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("5569999999999", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());
        var template = root.GetProperty("template");
        Assert.Equal("professional_client_checked_in", template.GetProperty("name").GetString());
        Assert.Equal("pt_BR", template.GetProperty("language").GetProperty("code").GetString());
        var component = Assert.Single(template.GetProperty("components").EnumerateArray());
        Assert.Equal("body", component.GetProperty("type").GetString());
        Assert.Equal(["Dra. Ana", "João", "14:30"],
            component.GetProperty("parameters").EnumerateArray().Select(x =>
            {
                Assert.Equal("text", x.GetProperty("type").GetString());
                return x.GetProperty("text").GetString();
            }));
        Assert.False(root.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task A_template_url_button_parameter_is_sent_as_the_first_url_button()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.TPL2"}]}""");
        var service = CreateService(handler, ConfiguredOptions());

        await service.SendTemplateAsync("+5569999999999",
            new WhatsAppTemplate("client_professional_cancelled_reschedule", "pt_BR", ["Ana"], "tok-123"),
            CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var components = body.RootElement.GetProperty("template").GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal(2, components.Length);
        var button = components[1];
        Assert.Equal("button", button.GetProperty("type").GetString());
        Assert.Equal("url", button.GetProperty("sub_type").GetString());
        Assert.Equal("0", button.GetProperty("index").GetString());
        Assert.Equal("tok-123", Assert.Single(button.GetProperty("parameters").EnumerateArray()).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Template_parameters_are_flattened_to_a_single_line()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.TPL3"}]}""");
        var service = CreateService(handler, ConfiguredOptions());

        await service.SendTemplateAsync("+5569999999999",
            new WhatsAppTemplate("t", "pt_BR", ["Ana\nMaria\t  Souza"]), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var parameter = body.RootElement.GetProperty("template").GetProperty("components")[0]
            .GetProperty("parameters")[0].GetProperty("text").GetString();
        Assert.Equal("Ana Maria Souza", parameter);
    }

    [Theory]
    [InlineData("", "pt_BR")]
    [InlineData("Nome Com Espaço", "pt_BR")]
    [InlineData("valid_name", "")]
    [InlineData("valid_name", "portuguese")]
    public async Task Invalid_template_name_or_language_is_rejected_without_calling_the_api(string name, string language)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTemplateAsync("+5569999999999", new WhatsAppTemplate(name, language, ["x"]),
            CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.InvalidMessage, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_empty_template_parameter_is_rejected_without_calling_the_api()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTemplateAsync("+5569999999999", new WhatsAppTemplate("t", "pt_BR", ["Ana", " "]),
            CancellationToken.None);

        Assert.Equal(WhatsAppFailureCodes.InvalidMessage, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Template_send_fails_closed_when_not_configured_or_the_recipient_is_invalid()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var unconfigured = ConfiguredOptions();
        unconfigured.AccessToken = "";
        var template = new WhatsAppTemplate("t", "pt_BR", ["x"]);

        Assert.Equal(WhatsAppFailureCodes.NotConfigured,
            (await CreateService(handler, unconfigured).SendTemplateAsync("+5569999999999", template, CancellationToken.None)).FailureCode);
        Assert.Equal(WhatsAppFailureCodes.InvalidRecipient,
            (await CreateService(handler, ConfiguredOptions()).SendTemplateAsync("123", template, CancellationToken.None)).FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_rejected_template_maps_the_meta_error_like_a_text_message()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, """{"error":{"code":131000,"fbtrace_id":"T"}}""");
        var service = CreateService(handler, ConfiguredOptions());

        var result = await service.SendTemplateAsync("+5569999999999", new WhatsAppTemplate("t", "pt_BR", ["x"]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(WhatsAppFailureCodes.ProviderUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task Template_logs_never_contain_the_token_the_recipient_or_the_parameters()
    {
        var logs = new ListLoggerProvider();
        foreach (var (status, body) in new[]
                 {
                     (HttpStatusCode.OK, """{"messages":[{"id":"wamid.OK"}]}"""),
                     (HttpStatusCode.BadRequest, """{"error":{"code":132001,"fbtrace_id":"T"}}""")
                 })
        {
            var service = CreateService(new FakeHandler(status, body), ConfiguredOptions(), logs);
            await service.SendTemplateAsync("+5569988887777",
                new WhatsAppTemplate("t", "pt_BR", ["Parametro Secreto"], "botao-secreto"), CancellationToken.None);
        }

        var text = logs.Text;
        Assert.NotEmpty(text);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("988887777", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Parametro Secreto", text, StringComparison.Ordinal);
        Assert.DoesNotContain("botao-secreto", text, StringComparison.Ordinal);
    }

    private static WhatsAppCloudOptions ConfiguredOptions() => new()
    {
        PhoneNumberId = "1004068049466823",
        AccessToken = Token,
        ApiVersion = "v99.0",
        BaseUrl = "https://graph.example.test",
        TimeoutSeconds = 10
    };

    private static WhatsAppCloudApiService CreateService(FakeHandler handler, WhatsAppCloudOptions options,
        ILoggerProvider? logs = null, RecordingMessageStore? store = null)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null) builder.AddProvider(logs);
        });
        return new WhatsAppCloudApiService(new HttpClient(handler), new StaticOptionsMonitor(options),
            store ?? new RecordingMessageStore(), TimeProvider.System,
            loggerFactory.CreateLogger<WhatsAppCloudApiService>());
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string? AuthorizationScheme,
        string? AuthorizationParameter, string Body);

    private sealed class FakeHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;
        public Exception? Throw { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(),
                request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter, body));
            if (Throw is not null) throw Throw;
            if (Delay != TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>The send path persists the accepted wamid; here it only has to not get in the way.</summary>
    private sealed class RecordingMessageStore : IWhatsAppMessageStore
    {
        public List<(string MessageId, Domain.Whatsapp.WhatsAppMessageType Type)> Accepted { get; } = [];

        public Task RecordAcceptedAsync(string messageId, string recipientPhone, string? phoneNumberId,
            Domain.Whatsapp.WhatsAppMessageType messageType, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            Accepted.Add((messageId, messageType));
            return Task.CompletedTask;
        }

        public Task<bool> ApplyStatusAsync(WhatsAppStatusUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class StaticOptionsMonitor(WhatsAppCloudOptions value) : IOptionsMonitor<WhatsAppCloudOptions>
    {
        public WhatsAppCloudOptions CurrentValue => value;
        public WhatsAppCloudOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<WhatsAppCloudOptions, string?> listener) => null;
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly List<string> lines = [];
        public string Text { get { lock (lines) return string.Join('\n', lines); } }
        public ILogger CreateLogger(string categoryName) => new ListLogger(this);
        public void Dispose() { }

        private sealed class ListLogger(ListLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner.lines) owner.lines.Add($"{formatter(state, exception)} {exception}");
            }
        }
    }
}
