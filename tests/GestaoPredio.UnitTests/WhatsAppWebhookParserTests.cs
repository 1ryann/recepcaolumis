using System.Text;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Whatsapp;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppWebhookParserTests
{
    private const string StatusPayload = """
        {"object":"whatsapp_business_account","entry":[{"id":"1500039464855591","changes":[{"field":"statuses","value":{
          "messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"+55 11 99382-8941","phone_number_id":"1004060849466823"},
          "statuses":[{"id":"wamid.AAA","status":"sent","timestamp":"1789670000","recipient_id":"5569999538007",
            "conversation":{"id":"conv.1","origin":{"type":"utility"}},
            "pricing":{"billable":true,"category":"utility"}}]}}]}]}
        """;

    [Fact]
    public void Parses_a_sent_status_with_ids_recipient_timestamp_and_context()
    {
        var result = WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(StatusPayload));

        Assert.True(result.Parsed);
        Assert.Equal("whatsapp_business_account", result.Object);
        Assert.Equal(1, result.EntryCount);
        Assert.Equal(0, result.InboundMessageCount);
        var status = Assert.Single(result.Statuses);
        Assert.Equal("wamid.AAA", status.MessageId);
        Assert.Equal(WhatsAppDeliveryStatus.Sent, status.Status);
        Assert.Equal("5569999538007", status.RecipientId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789670000), status.OccurredAt);
        Assert.Equal("1004060849466823", status.PhoneNumberId);
        Assert.Equal("1500039464855591", status.WhatsAppBusinessAccountId);
        Assert.Null(status.ErrorCode);
        Assert.Null(status.ErrorTitle);
    }

    [Fact]
    public void Carries_the_echoed_callback_data_to_the_store_and_nothing_when_absent()
    {
        var payload = StatusPayload.Replace("\"status\":\"sent\"",
            "\"status\":\"sent\",\"biz_opaque_callback_data\":\"lumis-notification:0123456789abcdef0123456789abcdef\"",
            StringComparison.Ordinal);

        var withCallback = Assert.Single(WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload)).Statuses);
        var withoutCallback = Assert.Single(WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(StatusPayload)).Statuses);

        Assert.Equal("lumis-notification:0123456789abcdef0123456789abcdef", withCallback.CallbackData);
        Assert.Equal(withCallback.CallbackData, withCallback.ToUpdate().CallbackData);
        Assert.Null(withoutCallback.CallbackData);
        Assert.Null(withoutCallback.ToUpdate().CallbackData);
    }

    [Theory]
    [InlineData("sent", WhatsAppDeliveryStatus.Sent)]
    [InlineData("delivered", WhatsAppDeliveryStatus.Delivered)]
    [InlineData("read", WhatsAppDeliveryStatus.Read)]
    [InlineData("failed", WhatsAppDeliveryStatus.Failed)]
    [InlineData("deleted", WhatsAppDeliveryStatus.Unknown)]
    public void Maps_every_documented_status_value(string raw, WhatsAppDeliveryStatus expected)
    {
        var payload = StatusPayload.Replace("\"status\":\"sent\"", $"\"status\":\"{raw}\"", StringComparison.Ordinal);

        var status = Assert.Single(WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload)).Statuses);

        Assert.Equal(expected, status.Status);
        Assert.Equal(raw, status.RawStatus);
    }

    [Fact]
    public void Parses_a_failed_status_with_the_meta_error_code_and_title()
    {
        var payload = """
            {"object":"whatsapp_business_account","entry":[{"id":"1500039464855591","changes":[{"field":"statuses","value":{
              "metadata":{"phone_number_id":"1004060849466823"},
              "statuses":[{"id":"wamid.FAIL","status":"failed","timestamp":"1789670500","recipient_id":"5569999538007",
                "errors":[{"code":131026,"title":"Message undeliverable","message":"Message undeliverable",
                  "error_data":{"details":"Receiver is incapable of receiving this message"}}]}]}}]}]}
            """;

        var status = Assert.Single(WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload)).Statuses);

        Assert.Equal(WhatsAppDeliveryStatus.Failed, status.Status);
        Assert.Equal("wamid.FAIL", status.MessageId);
        Assert.Equal(131026, status.ErrorCode);
        Assert.Equal("Message undeliverable", status.ErrorTitle);
        Assert.Equal("Receiver is incapable of receiving this message", status.ErrorDetails);
    }

    [Fact]
    public void Counts_inbound_messages_without_exposing_their_content()
    {
        var payload = """
            {"object":"whatsapp_business_account","entry":[{"id":"1500039464855591","changes":[{"field":"messages","value":{
              "metadata":{"phone_number_id":"1004060849466823"},
              "contacts":[{"profile":{"name":"Cliente"},"wa_id":"5569977776666"}],
              "messages":[{"from":"5569977776666","id":"wamid.IN","timestamp":"1","type":"text","text":{"body":"conteudo privado"}}]}}]}]}
            """;

        var result = WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload));

        Assert.True(result.Parsed);
        Assert.Equal(1, result.InboundMessageCount);
        Assert.Empty(result.Statuses);
        Assert.DoesNotContain("conteudo privado", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Handles_several_entries_changes_and_statuses_in_one_delivery()
    {
        var payload = """
            {"object":"whatsapp_business_account","entry":[
              {"id":"1500039464855591","changes":[{"field":"statuses","value":{"metadata":{"phone_number_id":"1004060849466823"},
                "statuses":[{"id":"wamid.A","status":"sent","timestamp":"1","recipient_id":"1"},
                            {"id":"wamid.A","status":"delivered","timestamp":"2","recipient_id":"1"}]}}]},
              {"id":"1500039464855591","changes":[{"field":"statuses","value":{"metadata":{"phone_number_id":"1004060849466823"},
                "statuses":[{"id":"wamid.B","status":"read","timestamp":"3","recipient_id":"2"}]}}]}]}
            """;

        var result = WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload));

        Assert.Equal(2, result.EntryCount);
        Assert.Equal(3, result.Statuses.Count);
        Assert.Equal([WhatsAppDeliveryStatus.Sent, WhatsAppDeliveryStatus.Delivered, WhatsAppDeliveryStatus.Read],
            result.Statuses.Select(x => x.Status));
    }

    [Fact]
    public void Statuses_without_an_id_or_status_are_ignored()
    {
        var payload = """
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"statuses","value":{
              "statuses":[{"status":"sent","timestamp":"1"},{"id":"wamid.NOSTATUS","timestamp":"1"},
                          {"id":"wamid.OK","status":"sent","timestamp":"1"}]}}]}]}
            """;

        var status = Assert.Single(WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload)).Statuses);

        Assert.Equal("wamid.OK", status.MessageId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Malformed_or_non_object_payloads_are_reported_as_not_parsed(string payload)
    {
        Assert.False(WhatsAppWebhookParser.Parse(Encoding.UTF8.GetBytes(payload)).Parsed);
    }

    [Fact]
    public void Recorder_keeps_the_latest_events_and_ignores_repeated_deliveries()
    {
        var recorder = new WhatsAppStatusRecorder();
        var first = new WhatsAppStatusEvent("wamid.AAA", "sent", WhatsAppDeliveryStatus.Sent, "5569999538007",
            DateTimeOffset.FromUnixTimeSeconds(1), "1004060849466823", "1500039464855591", null, null, null);

        Assert.True(recorder.TryRecord(first));
        Assert.False(recorder.TryRecord(first));
        Assert.True(recorder.TryRecord(first with { RawStatus = "delivered", Status = WhatsAppDeliveryStatus.Delivered }));
        Assert.Equal(2, recorder.Recent.Count);
        Assert.Equal("wamid.AAA", recorder.Recent[0].MessageId);
    }

    [Fact]
    public void Recorder_is_bounded_so_a_webhook_flood_cannot_grow_memory_without_limit()
    {
        var recorder = new WhatsAppStatusRecorder();

        for (var i = 0; i < WhatsAppStatusRecorder.Capacity + 50; i++)
            Assert.True(recorder.TryRecord(new WhatsAppStatusEvent($"wamid.{i}", "sent", WhatsAppDeliveryStatus.Sent,
                "5569999538007", DateTimeOffset.FromUnixTimeSeconds(i + 1), "1", "2", null, null, null)));

        Assert.Equal(WhatsAppStatusRecorder.Capacity, recorder.Recent.Count);
        Assert.Equal($"wamid.{WhatsAppStatusRecorder.Capacity + 49}", recorder.Recent[^1].MessageId);
    }

    [Theory]
    [InlineData("5569999538007", "***8007")]
    [InlineData("007", "***")]
    [InlineData("", "***")]
    public void Recipient_is_masked_for_logging(string recipient, string expected)
    {
        Assert.Equal(expected, WhatsAppStatusEvent.MaskRecipient(recipient));
    }
}
