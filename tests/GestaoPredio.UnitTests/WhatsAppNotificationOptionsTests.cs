using GestaoPredio.Domain.Notifications;
using GestaoPredio.Infrastructure.Notifications;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppNotificationOptionsTests
{
    [Theory]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 600)]
    [InlineData(9, 600)]   // the last delay repeats
    public void Default_backoff_is_30s_then_2min_then_10min(int nextAttempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), new WhatsAppNotificationOptions().RetryDelayBefore(nextAttempt));
    }

    [Fact]
    public void Configured_backoff_replaces_the_defaults()
    {
        var options = new WhatsAppNotificationOptions { RetryDelaysSeconds = [5, 7] };

        Assert.Equal(TimeSpan.FromSeconds(5), options.RetryDelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(7), options.RetryDelayBefore(3));
        Assert.Equal(TimeSpan.FromSeconds(7), options.RetryDelayBefore(5));
    }

    [Fact]
    public void Dispatch_is_off_by_default()
    {
        Assert.False(new WhatsAppNotificationOptions().Enabled);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(new WhatsAppNotificationOptionsValidator().Validate(null, new WhatsAppNotificationOptions()).Succeeded);
    }

    [Theory]
    [InlineData(nameof(WhatsAppNotificationOptions.MaxAttempts), 0)]
    [InlineData(nameof(WhatsAppNotificationOptions.LeaseSeconds), 5)]
    [InlineData(nameof(WhatsAppNotificationOptions.BatchSize), 0)]
    public void Out_of_range_values_are_rejected_naming_the_key(string property, int value)
    {
        var options = new WhatsAppNotificationOptions();
        typeof(WhatsAppNotificationOptions).GetProperty(property)!.SetValue(options, value);

        var result = new WhatsAppNotificationOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(property, result.FailureMessage);
    }

    [Fact]
    public void Template_names_are_resolved_per_type_and_blank_means_not_configured()
    {
        var templates = new WhatsAppTemplateOptions { ClientCheckedIn = "  professional_client_checked_in  " };

        Assert.Equal("professional_client_checked_in", templates.NameFor(WhatsAppNotificationType.ClientCheckedIn));
        Assert.Equal("", templates.NameFor(WhatsAppNotificationType.ProfessionalDelayed));
    }

    [Theory]
    [InlineData("Maria Clara Souza", "Maria")]
    [InlineData("  ", "cliente")]
    [InlineData(null, "cliente")]
    public void Only_the_first_name_leaves_the_system(string? name, string expected)
    {
        Assert.Equal(expected, WhatsAppNotificationComposer.FirstName(name));
    }
}
