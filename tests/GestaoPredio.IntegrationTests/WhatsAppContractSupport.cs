using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Infrastructure.Whatsapp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Shared Cloud API contract for the approved WhatsApp templates: a host with the real WhatsAppCloudApiService whose
/// network is replaced by <see cref="RecordingGraph"/>, and the assertions on the exact request. Nothing reaches Meta.
/// </summary>
internal static class WhatsAppContractSupport
{
    public const string PhoneNumberId = "1004060849466823";
    public const string GraphBase = "https://graph.contract.test";
    public const string AccessToken = "contract-test-access-token-not-real";
    public const string AppSecret = "contract-test-app-secret-not-real";

    /// <summary>The template names approved in Meta, configured exactly as they will be in Railway.</summary>
    public static readonly (string Key, string Value)[] ApprovedTemplates =
    [
        ("Whatsapp:Templates:ClientCheckedIn", "professional_client_checked_in"),
        ("Whatsapp:Templates:ProfessionalDelayed", "client_professional_delayed"),
        ("Whatsapp:Templates:ProfessionalCancelled", "client_professional_cancelled"),
        ("Whatsapp:Templates:AppointmentRescheduled", "appointment_rescheduled"),
        ("Whatsapp:Templates:AppointmentConfirmed", "appointment_confirmed"),
        ("Whatsapp:Templates:AppointmentCancelled", "appointment_cancelled")
    ];

    public static WebApplicationFactory<recepcaototem.Pages.IndexModel> CreateHost(ModulesApiFactory factory, RecordingGraph graph,
        params (string Key, string Value)[] extra) =>
        factory.WithWebHostBuilder(builder =>
        {
            (string Key, string Value)[] cloud =
            [
                ("Whatsapp:PhoneNumberId", PhoneNumberId), ("Whatsapp:AccessToken", AccessToken), ("Whatsapp:ApiVersion", "v26.0"),
                ("Whatsapp:BaseUrl", GraphBase), ("Whatsapp:TimeoutSeconds", "10"), ("Whatsapp:AppSecret", AppSecret)
            ];
            foreach (var (key, value) in cloud.Concat(ApprovedTemplates).Concat(extra)) builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>().ConfigurePrimaryHttpMessageHandler(() => graph));
        });

    /// <summary>Everything outside the template: endpoint, bearer token, recipient, product, callback data.</summary>
    public static void AssertEnvelope((string Uri, string? Authorization, JsonObject Body) request, string phone, WhatsAppNotification notice)
    {
        Assert.Equal($"{GraphBase}/v26.0/{PhoneNumberId}/messages", request.Uri);
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
        var body = request.Body;
        Assert.Equal(["biz_opaque_callback_data", "messaging_product", "recipient_type", "template", "to", "type"],
            body.Select(x => x.Key).Order());
        Assert.Equal("whatsapp", (string?)body["messaging_product"]);
        Assert.Equal("individual", (string?)body["recipient_type"]);
        Assert.Equal("template", (string?)body["type"]);
        Assert.Equal(phone.TrimStart('+'), (string?)body["to"]);
        Assert.Equal(notice.CallbackData, (string?)body["biz_opaque_callback_data"]);
    }

    /// <summary>The template object must be exactly this: name, pt_BR, positional body parameters, optional URL button 0.</summary>
    public static void AssertTemplate((string Uri, string? Authorization, JsonObject Body) request, string name,
        string[] bodyParameters, string? button)
    {
        var components = new JsonArray(new JsonObject
        {
            ["type"] = "body",
            ["parameters"] = new JsonArray(bodyParameters.Select(text => (JsonNode)new JsonObject { ["text"] = text, ["type"] = "text" }).ToArray())
        });
        if (button is not null)
            components.Add(new JsonObject
            {
                ["type"] = "button", ["sub_type"] = "url", ["index"] = "0",
                ["parameters"] = new JsonArray(new JsonObject { ["text"] = button, ["type"] = "text" })
            });
        var expected = new JsonObject
        {
            ["name"] = name,
            ["language"] = new JsonObject { ["code"] = "pt_BR" },
            ["components"] = components
        };
        var actual = request.Body["template"];
        Assert.True(JsonNode.DeepEquals(expected, actual), $"template payload differs:\nexpected {expected.ToJsonString()}\nactual   {actual?.ToJsonString()}");
    }

    public static string ButtonParameter((string Uri, string? Authorization, JsonObject Body) request) =>
        (string)request.Body["template"]!["components"]!.AsArray()
            .Single(x => (string?)x!["type"] == "button")!["parameters"]![0]!["text"]!;
}

/// <summary>Records every Cloud API request and answers from a script (default: accepted with a fresh wamid).</summary>
internal sealed class RecordingGraph : HttpMessageHandler
{
    private int counter;
    public List<(string Uri, string? Authorization, JsonObject Body)> Requests { get; } = [];
    public Queue<(HttpStatusCode Status, string Body)> Script { get; } = new();
    public TimeSpan Delay { get; init; }
    public Func<Task>? OnRequest { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), JsonNode.Parse(body)!.AsObject()));
        if (OnRequest is { } onRequest) await onRequest();
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        var (status, response) = Script.Count > 0
            ? Script.Dequeue()
            : (HttpStatusCode.OK, $$"""{"messaging_product":"whatsapp","messages":[{"id":"wamid.CONTRACT.{{Interlocked.Increment(ref counter)}}"}]}""");
        return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }
}

