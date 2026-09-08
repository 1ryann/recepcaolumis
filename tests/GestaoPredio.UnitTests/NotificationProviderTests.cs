using GestaoPredio.Application.Notifications;
using GestaoPredio.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace GestaoPredio.UnitTests;

public sealed class NotificationProviderTests
{
    [Fact]
    public async Task Demo_provider_records_success_without_exposing_destination()
    {
        var recorder = new DemoNotificationRecorder();
        var configuration = new ConfigurationBuilder().Build();
        var provider = new DemoNotificationService(recorder, configuration);

        var result = await provider.SendAsync(new NotificationMessage(
            Guid.NewGuid(), "+5569999999999", NotificationEventTypes.ProfessionalVisitWaiting,
            "Novo cliente aguardando atendimento."), CancellationToken.None);

        Assert.True(result.Success);
        var attempt = Assert.Single(recorder.Attempts);
        Assert.Equal(NotificationEventTypes.ProfessionalVisitWaiting, attempt.EventType);
        Assert.DoesNotContain("5569999999999", string.Join('|', recorder.Attempts));
    }

    [Fact]
    public async Task Demo_provider_can_fail_closed_without_throwing()
    {
        var recorder = new DemoNotificationRecorder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Demo:ForceFailure"] = "true"
            }).Build();
        var provider = new DemoNotificationService(recorder, configuration);

        var result = await provider.SendAsync(new NotificationMessage(
            Guid.NewGuid(), "+5569999999999", NotificationEventTypes.ProfessionalVisitWaiting, "mensagem"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DEMO_PROVIDER_FAILURE", result.FailureCode);
        Assert.Equal(result.FailureCode, Assert.Single(recorder.Attempts).FailureCode);
    }

    [Fact]
    public async Task Demo_provider_records_a_customer_message_without_leaking_the_phone()
    {
        var recorder = new DemoNotificationRecorder();
        var provider = new DemoNotificationService(recorder, new ConfigurationBuilder().Build());

        var result = await provider.SendAsync(new NotificationMessage(
            null, "+5569988887777", NotificationEventTypes.CustomerReservationCancelledReschedule,
            "Seu atendimento precisou ser cancelado. https://x/reagendar/t") { CustomerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.Success);
        var attempt = Assert.Single(recorder.Attempts);
        Assert.Equal(NotificationEventTypes.CustomerReservationCancelledReschedule, attempt.EventType);
        Assert.DoesNotContain("5569988887777", string.Join('|', recorder.Attempts));
    }

    [Fact]
    public async Task Meta_adapter_is_fail_closed_without_external_call_when_unconfigured()
    {
        var provider = new MetaWhatsAppNotificationService(new ConfigurationBuilder().Build());

        var result = await provider.SendAsync(new NotificationMessage(
            Guid.NewGuid(), "+5569999999999", NotificationEventTypes.ProfessionalVisitWaiting, "mensagem"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("META_NOT_CONFIGURED", result.FailureCode);
    }
}
