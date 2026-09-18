namespace GestaoPredio.Application.Whatsapp;

/// <summary>Stable, provider-agnostic failure codes. Never carry provider free text, tokens or recipients.</summary>
public static class WhatsAppFailureCodes
{
    public const string NotConfigured = "WHATSAPP_NOT_CONFIGURED";
    public const string InvalidRecipient = "WHATSAPP_RECIPIENT_INVALID";
    public const string InvalidMessage = "WHATSAPP_MESSAGE_INVALID";
    public const string AuthenticationFailed = "WHATSAPP_AUTH_FAILED";
    public const string RecipientNotAllowed = "WHATSAPP_RECIPIENT_NOT_ALLOWED";
    public const string OutsideServiceWindow = "WHATSAPP_OUTSIDE_SERVICE_WINDOW";
    public const string RateLimited = "WHATSAPP_RATE_LIMITED";
    public const string RequestRejected = "WHATSAPP_REQUEST_REJECTED";
    public const string ProviderUnavailable = "WHATSAPP_PROVIDER_UNAVAILABLE";
    public const string Timeout = "WHATSAPP_TIMEOUT";
    public const string NetworkError = "WHATSAPP_NETWORK_ERROR";
    public const string InvalidResponse = "WHATSAPP_INVALID_RESPONSE";
}

public sealed record WhatsAppSendResult(bool Success, string? MessageId, string? FailureCode, int? ProviderErrorCode)
{
    public static WhatsAppSendResult Succeeded(string messageId) => new(true, messageId, null, null);
    public static WhatsAppSendResult Failed(string failureCode, int? providerErrorCode = null) =>
        new(false, null, failureCode, providerErrorCode);
}

/// <summary>
/// The single entry point for outbound WhatsApp Cloud API calls. Callers never see the Graph API,
/// the access token or the phone number id.
/// </summary>
public interface IWhatsAppService
{
    Task<WhatsAppSendResult> SendTextAsync(string destinationPhone, string body, CancellationToken cancellationToken);
}
