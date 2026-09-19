using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Whatsapp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Whatsapp;

/// <summary>
/// Typed HttpClient over the WhatsApp Cloud API "messages" endpoint. The bearer token is attached per
/// request (never to DefaultRequestHeaders) and is never logged; logs carry only failure codes, Meta's
/// numeric error code and fbtrace_id — no recipient, body or provider free text.
/// </summary>
public sealed class WhatsAppCloudApiService(
    HttpClient httpClient,
    IOptionsMonitor<WhatsAppCloudOptions> options,
    IWhatsAppMessageStore messages,
    TimeProvider timeProvider,
    ILogger<WhatsAppCloudApiService> logger) : IWhatsAppService
{
    public const int MaxTextLength = 4096;
    public const int MaxTemplateParameterLength = 1024;
    public const int MaxTemplateParameters = 10;

    private static readonly Regex TemplateName = new("^[a-z0-9_]{1,512}$", RegexOptions.CultureInvariant);
    private static readonly Regex TemplateLanguage = new("^[a-z]{2,3}(_[A-Z]{2})?$", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex CallbackData = new(@"^\S{1,512}$", RegexOptions.CultureInvariant);

    public async Task<WhatsAppSendResult> SendTextAsync(string destinationPhone, string body, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.IsSendConfigured)
            return Fail(WhatsAppFailureCodes.NotConfigured);
        if (!WhatsAppNormalizer.TryNormalize(destinationPhone, out var e164))
            return Fail(WhatsAppFailureCodes.InvalidRecipient);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxTextLength)
            return Fail(WhatsAppFailureCodes.InvalidMessage);

        return await PostAsync(settings, e164, JsonContent.Create(new TextMessageRequest(e164.TrimStart('+'), new TextBody(body))),
            WhatsAppMessageType.Text, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendTemplateAsync(string destinationPhone, WhatsAppTemplate template,
        CancellationToken cancellationToken) =>
        SendTemplateAsync(destinationPhone, template, null, cancellationToken);

    public async Task<WhatsAppSendResult> SendTemplateAsync(string destinationPhone, WhatsAppTemplate template,
        string? callbackData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        var settings = options.CurrentValue;
        if (!settings.IsSendConfigured)
            return Fail(WhatsAppFailureCodes.NotConfigured);
        if (!WhatsAppNormalizer.TryNormalize(destinationPhone, out var e164))
            return Fail(WhatsAppFailureCodes.InvalidRecipient);
        if (!TryBuildComponents(template, out var components) ||
            (callbackData is not null && !CallbackData.IsMatch(callbackData)))
            return Fail(WhatsAppFailureCodes.InvalidMessage);

        var content = JsonContent.Create(new TemplateMessageRequest(e164.TrimStart('+'),
            new TemplateBody(template.Name, new TemplateLanguageBody(template.LanguageCode), components), callbackData));
        return await PostAsync(settings, e164, content, WhatsAppMessageType.Template, cancellationToken);
    }

    /// <summary>
    /// Meta rejects template parameters with new lines, tabs or runs of spaces, so each one is flattened to a single
    /// line. An empty or oversized parameter, or an invalid name or language, fails closed before any HTTP call.
    /// </summary>
    private static bool TryBuildComponents(WhatsAppTemplate template, out List<TemplateComponent> components)
    {
        components = [];
        if (template.Name is null || !TemplateName.IsMatch(template.Name) ||
            template.LanguageCode is null || !TemplateLanguage.IsMatch(template.LanguageCode) ||
            template.BodyParameters is null || template.BodyParameters.Count > MaxTemplateParameters)
            return false;

        var body = new List<TemplateParameter>();
        foreach (var raw in template.BodyParameters)
        {
            if (!TryFlatten(raw, out var value)) return false;
            body.Add(new TemplateParameter(value));
        }
        if (body.Count > 0) components.Add(new TemplateComponent("body", null, null, body));

        if (template.UrlButtonParameter is not null)
        {
            if (!TryFlatten(template.UrlButtonParameter, out var suffix)) return false;
            components.Add(new TemplateComponent("button", "url", "0", [new TemplateParameter(suffix)]));
        }
        return true;
    }

    private static bool TryFlatten(string? raw, out string value)
    {
        value = Whitespace.Replace(raw ?? "", " ").Trim();
        return value.Length is >= 1 and <= MaxTemplateParameterLength;
    }

    private async Task<WhatsAppSendResult> PostAsync(WhatsAppCloudOptions settings, string e164, HttpContent content,
        WhatsAppMessageType messageType, CancellationToken cancellationToken)
    {
        var uri = $"{settings.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(settings.ApiVersion)}/{Uri.EscapeDataString(settings.PhoneNumberId)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            var result = response.IsSuccessStatusCode
                ? ReadSuccess(responseBody, messageType)
                : ReadError(response.StatusCode, responseBody);
            if (result is { Success: true, MessageId: { } messageId })
                await RecordAcceptedAsync(messageId, e164, settings.PhoneNumberId, messageType, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request may already be with Meta: a timeout is an unknown outcome, not a failed send.
            return Fail(WhatsAppFailureCodes.Timeout);
        }
        catch (HttpRequestException exception)
        {
            return Fail(exception.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
                or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError
                ? WhatsAppFailureCodes.NetworkError      // no connection: the request never left
                : WhatsAppFailureCodes.OutcomeUnknown);  // broken after sending: Meta may have the message
        }
    }

    /// <summary>
    /// Durable record of the wamid, so a webhook status can be matched even after a restart. The webhook may already
    /// have created the row; the store reconciles instead of duplicating. Meta already accepted the message, so a
    /// failure here must not turn the send into a failure (the caller would send it again): the webhook creates the
    /// row on the first status instead.
    /// </summary>
    private async Task RecordAcceptedAsync(string messageId, string e164, string phoneNumberId, WhatsAppMessageType messageType,
        CancellationToken cancellationToken)
    {
        try
        {
            await messages.RecordAcceptedAsync(messageId, e164, phoneNumberId, messageType, timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("WhatsApp message accepted but its record could not be stored now; the webhook will create it. MessageId: {MessageId}; Error: {Error}",
                messageId, exception.GetType().Name);
        }
    }

    private WhatsAppSendResult ReadSuccess(string content, WhatsAppMessageType messageType)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0 &&
                messages[0].TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                var messageId = id.GetString()!;
                logger.LogInformation("WhatsApp message accepted. MessageType: {MessageType}; MessageId: {MessageId}",
                    messageType, messageId);
                return WhatsAppSendResult.Succeeded(messageId);
            }
        }
        catch (JsonException) { }
        return Fail(WhatsAppFailureCodes.InvalidResponse);
    }

    private WhatsAppSendResult ReadError(HttpStatusCode status, string content)
    {
        int? metaCode = null;
        string? traceId = null;
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var code) && code.TryGetInt32(out var parsed)) metaCode = parsed;
                if (error.TryGetProperty("fbtrace_id", out var trace) && trace.ValueKind == JsonValueKind.String)
                    traceId = trace.GetString() is { Length: <= 64 } value ? value : null;
            }
        }
        catch (JsonException) { }

        var failure = Map(status, metaCode);
        logger.LogWarning("WhatsApp send failed. FailureCode: {FailureCode}; HttpStatus: {HttpStatus}; MetaErrorCode: {MetaErrorCode}; FbTraceId: {FbTraceId}",
            failure, (int)status, metaCode, traceId);
        return WhatsAppSendResult.Failed(failure, metaCode);
    }

    // Meta error codes: https://developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes
    private static string Map(HttpStatusCode status, int? metaCode) => metaCode switch
    {
        0 or 190 => WhatsAppFailureCodes.AuthenticationFailed,
        131030 => WhatsAppFailureCodes.RecipientNotAllowed,
        131047 => WhatsAppFailureCodes.OutsideServiceWindow,
        4 or 80007 or 130429 or 131048 or 131056 => WhatsAppFailureCodes.RateLimited,
        _ when status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WhatsAppFailureCodes.AuthenticationFailed,
        _ when status == HttpStatusCode.TooManyRequests => WhatsAppFailureCodes.RateLimited,
        _ when (int)status >= 500 => WhatsAppFailureCodes.ProviderUnavailable,
        _ => WhatsAppFailureCodes.RequestRejected
    };

    private WhatsAppSendResult Fail(string failureCode)
    {
        logger.LogWarning("WhatsApp send failed. FailureCode: {FailureCode}", failureCode);
        return WhatsAppSendResult.Failed(failureCode);
    }

    private sealed record TextMessageRequest(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("text")] TextBody Text)
    {
        [JsonPropertyName("messaging_product")] public string MessagingProduct => "whatsapp";
        [JsonPropertyName("recipient_type")] public string RecipientType => "individual";
        [JsonPropertyName("type")] public string Type => "text";
    }

    private sealed record TextBody([property: JsonPropertyName("body")] string Body)
    {
        [JsonPropertyName("preview_url")] public bool PreviewUrl => false;
    }

    private sealed record TemplateMessageRequest(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("template")] TemplateBody Template,
        [property: JsonPropertyName("biz_opaque_callback_data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CallbackData)
    {
        [JsonPropertyName("messaging_product")] public string MessagingProduct => "whatsapp";
        [JsonPropertyName("recipient_type")] public string RecipientType => "individual";
        [JsonPropertyName("type")] public string Type => "template";
    }

    private sealed record TemplateBody(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("language")] TemplateLanguageBody Language,
        [property: JsonPropertyName("components")] IReadOnlyList<TemplateComponent> Components);

    private sealed record TemplateLanguageBody([property: JsonPropertyName("code")] string Code);

    private sealed record TemplateComponent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sub_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubType,
        [property: JsonPropertyName("index"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Index,
        [property: JsonPropertyName("parameters")] IReadOnlyList<TemplateParameter> Parameters);

    private sealed record TemplateParameter([property: JsonPropertyName("text")] string Text)
    {
        [JsonPropertyName("type")] public string Type => "text";
    }
}
