using GestaoPredio.Domain.Whatsapp;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppMessageTests
{
    private const string MessageId = "wamid.HBgMNTU2OTk5NTM4MDA3FQIAERgSMUZFRkQwNDFEMkE5QzA4NUEwAA==";
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Accepted_outbound_message_starts_with_the_send_response_data()
    {
        var message = WhatsAppMessage.CreateAccepted(MessageId, "+55 69 99953-8007", "1004060849466823", Now);

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(MessageId, message.MessageId);
        Assert.Equal("+5569999538007", message.RecipientPhone);
        Assert.Equal("1004060849466823", message.PhoneNumberId);
        Assert.Null(message.WhatsAppBusinessAccountId);
        Assert.Equal(WhatsAppMessageDirection.Outbound, message.Direction);
        Assert.Equal(WhatsAppMessageType.Text, message.MessageType);
        Assert.Equal(WhatsAppDeliveryStatus.Accepted, message.Status);
        Assert.Equal(Now, message.LastStatusAt);
        Assert.Equal(Now, message.CreatedAt);
        Assert.Equal(Now, message.UpdatedAt);
        Assert.Null(message.ErrorCode);
    }

    [Fact]
    public void Message_created_from_a_webhook_status_keeps_the_ids_and_the_reported_instant()
    {
        var reportedAt = Now.AddMinutes(-5);

        var message = WhatsAppMessage.CreateFromStatus(MessageId, WhatsAppDeliveryStatus.Delivered, "5569999538007",
            "1004060849466823", "1500039464855591", reportedAt, null, null, null, Now);

        Assert.Equal(WhatsAppDeliveryStatus.Delivered, message.Status);
        Assert.Equal("+5569999538007", message.RecipientPhone);
        Assert.Equal("1500039464855591", message.WhatsAppBusinessAccountId);
        Assert.Equal(WhatsAppMessageDirection.Outbound, message.Direction);
        Assert.Equal(WhatsAppMessageType.Unknown, message.MessageType);
        Assert.Equal(reportedAt, message.LastStatusAt);
        Assert.Equal(Now, message.CreatedAt);
    }

    [Theory]
    [InlineData("", "+5569999538007")]
    [InlineData("wamid.X", "")]
    [InlineData("wamid.X", "abc")]
    public void Invalid_identifiers_are_rejected(string messageId, string recipient)
    {
        Assert.Throws<ArgumentException>(() =>
            WhatsAppMessage.CreateAccepted(messageId, recipient, "1004060849466823", Now));
    }

    [Theory]
    [InlineData(WhatsAppDeliveryStatus.Accepted, WhatsAppDeliveryStatus.Sent, true)]
    [InlineData(WhatsAppDeliveryStatus.Sent, WhatsAppDeliveryStatus.Delivered, true)]
    [InlineData(WhatsAppDeliveryStatus.Delivered, WhatsAppDeliveryStatus.Read, true)]
    [InlineData(WhatsAppDeliveryStatus.Accepted, WhatsAppDeliveryStatus.Read, true)]
    [InlineData(WhatsAppDeliveryStatus.Sent, WhatsAppDeliveryStatus.Failed, true)]
    [InlineData(WhatsAppDeliveryStatus.Read, WhatsAppDeliveryStatus.Delivered, false)]
    [InlineData(WhatsAppDeliveryStatus.Delivered, WhatsAppDeliveryStatus.Sent, false)]
    [InlineData(WhatsAppDeliveryStatus.Sent, WhatsAppDeliveryStatus.Sent, false)]
    [InlineData(WhatsAppDeliveryStatus.Sent, WhatsAppDeliveryStatus.Accepted, false)]
    [InlineData(WhatsAppDeliveryStatus.Failed, WhatsAppDeliveryStatus.Sent, false)]
    [InlineData(WhatsAppDeliveryStatus.Failed, WhatsAppDeliveryStatus.Read, false)]
    [InlineData(WhatsAppDeliveryStatus.Failed, WhatsAppDeliveryStatus.Failed, false)]
    public void Status_only_moves_forward_and_failed_is_terminal(
        WhatsAppDeliveryStatus current, WhatsAppDeliveryStatus incoming, bool expectedApplied)
    {
        var message = WhatsAppMessage.CreateFromStatus(MessageId, current, "5569999538007", "1", null,
            Now.AddMinutes(-10), null, null, null, Now.AddMinutes(-10));

        var applied = message.ApplyStatus(incoming, Now, null, null, null, Now);

        Assert.Equal(expectedApplied, applied);
        Assert.Equal(expectedApplied ? incoming : current, message.Status);
        Assert.Equal(expectedApplied ? Now : Now.AddMinutes(-10), message.LastStatusAt);
    }

    [Fact]
    public void Failed_status_stores_the_meta_error_fields()
    {
        var message = WhatsAppMessage.CreateAccepted(MessageId, "+5569999538007", "1", Now);

        Assert.True(message.ApplyStatus(WhatsAppDeliveryStatus.Failed, Now.AddMinutes(1), 131026,
            "Message undeliverable", "Receiver is incapable of receiving this message", Now.AddMinutes(1)));

        Assert.Equal(WhatsAppDeliveryStatus.Failed, message.Status);
        Assert.Equal(131026, message.ErrorCode);
        Assert.Equal("Message undeliverable", message.ErrorTitle);
        Assert.Equal("Receiver is incapable of receiving this message", message.ErrorDetails);
        Assert.Equal(Now.AddMinutes(1), message.UpdatedAt);
    }

    [Fact]
    public void Unknown_status_is_never_applied_and_does_not_throw()
    {
        var message = WhatsAppMessage.CreateAccepted(MessageId, "+5569999538007", "1", Now);

        Assert.False(message.ApplyStatus(WhatsAppDeliveryStatus.Unknown, Now.AddMinutes(1), null, null, null, Now.AddMinutes(1)));
        Assert.Equal(WhatsAppDeliveryStatus.Accepted, message.Status);
    }

    [Fact]
    public void Webhook_created_record_is_reconciled_with_the_send_response_without_regressing_the_status()
    {
        var message = WhatsAppMessage.CreateFromStatus(MessageId, WhatsAppDeliveryStatus.Delivered, "5569999538007",
            null, "1500039464855591", Now, null, null, null, Now);

        var changed = message.ReconcileAccepted("+55 69 99953-8007", "1004060849466823", WhatsAppMessageType.Text, Now.AddMinutes(1));

        Assert.True(changed);
        Assert.Equal(WhatsAppDeliveryStatus.Delivered, message.Status);
        Assert.Equal("1004060849466823", message.PhoneNumberId);
        Assert.Equal(WhatsAppMessageType.Text, message.MessageType);
        Assert.Equal("+5569999538007", message.RecipientPhone);
        Assert.Equal(Now.AddMinutes(1), message.UpdatedAt);
        Assert.Equal(Now, message.LastStatusAt);
    }

    [Fact]
    public void Reconciling_a_record_that_already_has_the_send_data_changes_nothing()
    {
        var message = WhatsAppMessage.CreateAccepted(MessageId, "+5569999538007", "1004060849466823", Now);

        Assert.False(message.ReconcileAccepted("+5569999538007", "1004060849466823", WhatsAppMessageType.Text, Now.AddMinutes(1)));
        Assert.Equal(Now, message.UpdatedAt);
    }

    [Fact]
    public void Recipient_reported_by_the_webhook_without_a_plus_sign_is_stored_in_e164()
    {
        var message = WhatsAppMessage.CreateFromStatus(MessageId, WhatsAppDeliveryStatus.Sent, "5569999538007",
            "1", null, Now, null, null, null, Now);

        Assert.Equal("+5569999538007", message.RecipientPhone);
    }

    [Fact]
    public void Error_text_is_bounded_so_a_long_provider_message_cannot_overflow_the_column()
    {
        var message = WhatsAppMessage.CreateAccepted(MessageId, "+5569999538007", "1", Now);

        message.ApplyStatus(WhatsAppDeliveryStatus.Failed, Now, 1, new string('t', 400), new string('d', 900), Now);

        Assert.Equal(200, message.ErrorTitle!.Length);
        Assert.Equal(500, message.ErrorDetails!.Length);
    }
}
