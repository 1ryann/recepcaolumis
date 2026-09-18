using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Whatsapp;

/// <summary>
/// WhatsApp Cloud API settings, bound to the same "Whatsapp" section that already carries
/// FinanceiroPhoneNumber (recepcaototem.Features.Rooms.WhatsappOptions). AccessToken, VerifyToken and
/// AppSecret are secrets: supply them only through user-secrets / environment variables, never appsettings.
/// Every value is optional so the application still starts with WhatsApp disabled (fail-closed).
/// </summary>
public sealed class WhatsAppCloudOptions
{
    public const string SectionName = "Whatsapp";
    public const string DefaultBaseUrl = "https://graph.facebook.com";

    public string PhoneNumberId { get; set; } = "";
    public string BusinessAccountId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ApiVersion { get; set; } = "";
    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public int TimeoutSeconds { get; set; } = 10;
    public string VerifyToken { get; set; } = "";
    public string AppSecret { get; set; } = "";

    public bool IsSendConfigured =>
        !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ApiVersion) && !string.IsNullOrWhiteSpace(BaseUrl);

    public bool IsWebhookVerificationConfigured => !string.IsNullOrWhiteSpace(VerifyToken);
    public bool IsWebhookSignatureConfigured => !string.IsNullOrWhiteSpace(AppSecret);

    // Keeps secrets out of any accidental ToString()/structured-log rendering of the options object.
    public override string ToString() => nameof(WhatsAppCloudOptions);
}

/// <summary>Format-only validation. Messages name the key, never its value.</summary>
public sealed partial class WhatsAppCloudOptionsValidator : IValidateOptions<WhatsAppCloudOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppCloudOptions options)
    {
        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.PhoneNumberId) && !Digits().IsMatch(options.PhoneNumberId))
            failures.Add("Whatsapp:PhoneNumberId deve conter somente dígitos.");
        if (!string.IsNullOrWhiteSpace(options.BusinessAccountId) && !Digits().IsMatch(options.BusinessAccountId))
            failures.Add("Whatsapp:BusinessAccountId deve conter somente dígitos.");
        if (!string.IsNullOrWhiteSpace(options.ApiVersion) && !ApiVersion().IsMatch(options.ApiVersion))
            failures.Add("Whatsapp:ApiVersion deve seguir o formato vNN.N.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            failures.Add("Whatsapp:BaseUrl deve ser uma URL HTTPS absoluta.");
        if (options.TimeoutSeconds is < 1 or > 60)
            failures.Add("Whatsapp:TimeoutSeconds deve estar entre 1 e 60.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[0-9]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex("^v[0-9]{1,3}\\.[0-9]{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiVersion();
}
