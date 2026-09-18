using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Infrastructure.Whatsapp;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppCloudOptionsTests
{
    [Fact]
    public void Empty_configuration_is_valid_so_the_application_starts_with_whatsapp_disabled()
    {
        var options = new WhatsAppCloudOptions { BaseUrl = "", ApiVersion = "" };

        var result = new WhatsAppCloudOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.False(options.IsSendConfigured);
        Assert.False(options.IsWebhookVerificationConfigured);
        Assert.False(options.IsWebhookSignatureConfigured);
    }

    [Fact]
    public void Default_base_url_is_the_official_graph_host_and_timeout_has_a_safe_default()
    {
        var options = new WhatsAppCloudOptions();

        Assert.Equal("https://graph.facebook.com", options.BaseUrl);
        Assert.Equal(10, options.TimeoutSeconds);
    }

    [Fact]
    public void Complete_send_configuration_is_reported_as_configured()
    {
        var options = new WhatsAppCloudOptions
        {
            PhoneNumberId = "1004068049466823", AccessToken = "x", ApiVersion = "v23.0"
        };

        Assert.True(new WhatsAppCloudOptionsValidator().Validate(null, options).Succeeded);
        Assert.True(options.IsSendConfigured);
    }

    [Theory]
    [InlineData("PhoneNumberId", "12ab")]
    [InlineData("BusinessAccountId", "lumis-hof")]
    [InlineData("ApiVersion", "23")]
    [InlineData("ApiVersion", "latest")]
    [InlineData("BaseUrl", "http://graph.facebook.com")]
    [InlineData("BaseUrl", "graph.facebook.com")]
    public void Malformed_values_are_rejected(string property, string value)
    {
        var options = new WhatsAppCloudOptions();
        typeof(WhatsAppCloudOptions).GetProperty(property)!.SetValue(options, value);

        var result = new WhatsAppCloudOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Timeout_outside_the_allowed_range_is_rejected(int seconds)
    {
        var result = new WhatsAppCloudOptionsValidator().Validate(null, new WhatsAppCloudOptions { TimeoutSeconds = seconds });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validation_failures_never_echo_secret_values()
    {
        var options = new WhatsAppCloudOptions
        {
            AccessToken = "secret-token-value", AppSecret = "secret-app-value", VerifyToken = "secret-verify-value",
            BaseUrl = "not a url", ApiVersion = "bad", PhoneNumberId = "bad", TimeoutSeconds = 0
        };

        var result = new WhatsAppCloudOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.DoesNotContain("secret-", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("not a url", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Webhook_signature_matches_the_hmac_sha256_of_the_raw_body()
    {
        var body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");
        var header = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("app-secret"), body));

        Assert.True(WhatsAppWebhookSecurity.IsSignatureValid(body, header, "app-secret"));
        Assert.True(WhatsAppWebhookSecurity.IsSignatureValid(body, header.ToUpperInvariant().Replace("SHA256=", "sha256="), "app-secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=abc")]
    [InlineData("sha256=")]
    [InlineData("sha256=zz")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public void Missing_or_wrong_webhook_signature_is_rejected(string? header)
    {
        var body = Encoding.UTF8.GetBytes("{}");

        Assert.False(WhatsAppWebhookSecurity.IsSignatureValid(body, header, "app-secret"));
    }

    [Fact]
    public void Signature_is_rejected_when_the_body_was_tampered_or_the_secret_is_not_configured()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var header = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("app-secret"), body));

        Assert.False(WhatsAppWebhookSecurity.IsSignatureValid(Encoding.UTF8.GetBytes("{\"a\":2}"), header, "app-secret"));
        Assert.False(WhatsAppWebhookSecurity.IsSignatureValid(body, header, ""));
        Assert.False(WhatsAppWebhookSecurity.IsSignatureValid(body, header, null));
    }

    [Theory]
    [InlineData("expected-token", "expected-token", true)]
    [InlineData("wrong-token", "expected-token", false)]
    [InlineData(null, "expected-token", false)]
    [InlineData("", "", false)]
    [InlineData("anything", null, false)]
    public void Verify_token_comparison_requires_a_configured_exact_match(string? provided, string? expected, bool valid)
    {
        Assert.Equal(valid, WhatsAppWebhookSecurity.IsVerifyTokenValid(provided, expected));
    }
}
