using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Professionals;
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

    public async Task<WhatsAppSendResult> SendTextAsync(string destinationPhone, string body, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.IsSendConfigured)
            return Fail(WhatsAppFailureCodes.NotConfigured);
        if (!WhatsAppNormalizer.TryNormalize(destinationPhone, out var e164))
            return Fail(WhatsAppFailureCodes.InvalidRecipient);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxTextLength)
            return Fail(WhatsAppFailureCodes.InvalidMessage);

        var uri = $"{settings.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(settings.ApiVersion)}/{Uri.EscapeDataString(settings.PhoneNumberId)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new TextMessageRequest(e164.TrimStart('+'), new TextBody(body)))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            var result = response.IsSuccessStatusCode ? ReadSuccess(content) : ReadError(response.StatusCode, content);
            if (result is { Success: true, MessageId: { } messageId })
                // Durable record of the wamid, so a webhook status can be matched even after a restart. The
                // webhook may already have created the row; the store reconciles instead of duplicating.
                await messages.RecordAcceptedAsync(messageId, e164, settings.PhoneNumberId, timeProvider.GetUtcNow(),
                    cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(WhatsAppFailureCodes.Timeout);
        }
        catch (HttpRequestException)
        {
            return Fail(WhatsAppFailureCodes.NetworkError);
        }
    }

    private WhatsAppSendResult ReadSuccess(string content)
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
                logger.LogInformation("WhatsApp text message accepted. MessageId: {MessageId}", messageId);
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
}
