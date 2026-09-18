using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Persistence of outbound messages and webhook statuses against the isolated test schema. Idempotency and the
/// status ordering must hold across processes, so every case goes through a fresh scope (new store instance).
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class WhatsappMessagePersistenceTests(ModulesApiFactory factory)
{
    private const string MessageId = "wamid.HBgMNTU2OTk5NTM4MDA3FQIAERgSMUZFRkQwNDFEMkE5QzA4NUEwAA==";
    private const string PhoneNumberId = "1004060849466823";
    private const string WabaId = "1500039464855591";
    // Just before the server's frozen clock, so server-stamped UpdatedAt is never earlier than CreatedAt.
    private static readonly DateTimeOffset Start = ModulesApiFactory.DefaultTestInstant.AddMinutes(-30);

    [Fact]
    public async Task Accepted_send_is_persisted_once_even_if_the_same_wamid_is_recorded_again()
    {
        await ResetAsync();

        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+55 69 99953-8007", PhoneNumberId, Start, default));
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+55 69 99953-8007", PhoneNumberId, Start.AddSeconds(5), default));

        var message = await SingleAsync();
        Assert.Equal(MessageId, message.MessageId);
        Assert.Equal("+5569999538007", message.RecipientPhone);
        Assert.Equal(PhoneNumberId, message.PhoneNumberId);
        Assert.Equal(WhatsAppMessageDirection.Outbound, message.Direction);
        Assert.Equal(WhatsAppMessageType.Text, message.MessageType);
        Assert.Equal(WhatsAppDeliveryStatus.Accepted, message.Status);
        Assert.Equal(Start, message.CreatedAt);
    }

    [Fact]
    public async Task Webhook_statuses_advance_the_persisted_record_through_sent_delivered_and_read()
    {
        await ResetAsync();
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default));

        foreach (var (status, minute) in new[]
                 {
                     (WhatsAppDeliveryStatus.Sent, 1), (WhatsAppDeliveryStatus.Delivered, 2), (WhatsAppDeliveryStatus.Read, 3)
                 })
        {
            var applied = await WithStoreAsync(store => store.ApplyStatusAsync(Update(status, Start.AddMinutes(minute)), default));
            Assert.True(applied);
            Assert.Equal(status, (await SingleAsync()).Status);
        }

        var message = await SingleAsync();
        Assert.Equal(Start.AddMinutes(3), message.LastStatusAt);
        Assert.True(message.UpdatedAt >= message.CreatedAt);
    }

    [Fact]
    public async Task Replayed_status_is_ignored_in_a_new_process_without_duplicating_the_row()
    {
        await ResetAsync();
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default));
        Assert.True(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Delivered, Start.AddMinutes(2)), default)));

        // A retry after a restart has no in-memory state to rely on: the database must reject it.
        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Delivered, Start.AddMinutes(2)), default)));
        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Sent, Start.AddMinutes(1)), default)));

        var message = await SingleAsync();
        Assert.Equal(WhatsAppDeliveryStatus.Delivered, message.Status);
        Assert.Equal(Start.AddMinutes(2), message.LastStatusAt);
    }

    [Fact]
    public async Task Out_of_order_delivery_never_moves_read_backwards()
    {
        await ResetAsync();
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default));
        Assert.True(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Read, Start.AddMinutes(3)), default)));

        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Delivered, Start.AddMinutes(9)), default)));
        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Sent, Start.AddMinutes(9)), default)));

        Assert.Equal(WhatsAppDeliveryStatus.Read, (await SingleAsync()).Status);
    }

    [Fact]
    public async Task Failed_status_persists_the_meta_error_and_is_not_overwritten_by_a_later_replay()
    {
        await ResetAsync();
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default));

        Assert.True(await WithStoreAsync(store => store.ApplyStatusAsync(
            Update(WhatsAppDeliveryStatus.Failed, Start.AddMinutes(1)) with
            {
                ErrorCode = 131026, ErrorTitle = "Message undeliverable",
                ErrorDetails = "Receiver is incapable of receiving this message"
            }, default)));
        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Sent, Start.AddMinutes(2)), default)));
        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Read, Start.AddMinutes(3)), default)));

        var message = await SingleAsync();
        Assert.Equal(WhatsAppDeliveryStatus.Failed, message.Status);
        Assert.Equal(131026, message.ErrorCode);
        Assert.Equal("Message undeliverable", message.ErrorTitle);
        Assert.Equal("Receiver is incapable of receiving this message", message.ErrorDetails);
    }

    [Fact]
    public async Task Webhook_arriving_before_the_send_record_creates_the_row_and_the_send_reconciles_it()
    {
        await ResetAsync();

        Assert.True(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Delivered, Start), default)));
        var created = await SingleAsync();
        Assert.Equal(WhatsAppDeliveryStatus.Delivered, created.Status);
        Assert.Equal(WhatsAppMessageType.Unknown, created.MessageType);
        Assert.Equal(WabaId, created.WhatsAppBusinessAccountId);

        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+55 69 99953-8007", PhoneNumberId, Start.AddSeconds(2), default));

        var reconciled = await SingleAsync();
        Assert.Equal(WhatsAppDeliveryStatus.Delivered, reconciled.Status);
        Assert.Equal(WhatsAppMessageType.Text, reconciled.MessageType);
        Assert.Equal(PhoneNumberId, reconciled.PhoneNumberId);
        Assert.Equal("+5569999538007", reconciled.RecipientPhone);
        Assert.Equal(created.Id, reconciled.Id);
    }

    [Fact]
    public async Task Concurrent_first_writes_for_the_same_wamid_keep_a_single_row()
    {
        await ResetAsync();

        await Task.WhenAll(
            WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default)),
            WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Sent, Start), default)),
            WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Sent, Start), default)),
            WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default)));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.WhatsAppMessages.CountAsync(x => x.MessageId == MessageId));
    }

    [Fact]
    public async Task Unknown_status_values_do_not_change_or_create_anything()
    {
        await ResetAsync();
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default));

        Assert.False(await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Unknown, Start.AddMinutes(1)), default)));

        Assert.Equal(WhatsAppDeliveryStatus.Accepted, (await SingleAsync()).Status);
    }

    [Fact]
    public async Task Persisted_row_carries_no_credential_no_payload_and_no_message_text()
    {
        await ResetAsync();
        await WithStoreAsync(store => store.RecordAcceptedAsync(MessageId, "+5569999538007", PhoneNumberId, Start, default));
        await WithStoreAsync(store => store.ApplyStatusAsync(Update(WhatsAppDeliveryStatus.Failed, Start.AddMinutes(1)) with
        {
            ErrorCode = 131026, ErrorTitle = "Message undeliverable", ErrorDetails = "details"
        }, default));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var columns = await db.Database.SqlQuery<string>(
            $"""SELECT column_name FROM information_schema.columns WHERE table_name = 'WhatsAppMessages'""").ToListAsync();

        Assert.DoesNotContain(columns, x => x.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, x => x.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, x => x.Contains("Payload", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, x => x.Contains("Body", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, x => x.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            ["CreatedAt", "Direction", "ErrorCode", "ErrorDetails", "ErrorTitle", "Id", "LastStatusAt", "MessageId",
             "MessageType", "PhoneNumberId", "RecipientPhone", "Status", "UpdatedAt", "WhatsAppBusinessAccountId"],
            columns.Order().ToArray());
    }

    private static WhatsAppStatusUpdate Update(WhatsAppDeliveryStatus status, DateTimeOffset reportedAt) =>
        new(MessageId, status, "5569999538007", reportedAt, PhoneNumberId, WabaId, null, null, null);

    private async Task ResetAsync()
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"WhatsAppMessages\"");
    }

    private async Task<WhatsAppMessage> SingleAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.WhatsAppMessages.AsNoTracking().SingleAsync(x => x.MessageId == MessageId);
    }

    private async Task<T> WithStoreAsync<T>(Func<IWhatsAppMessageStore, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IWhatsAppMessageStore>());
    }

    private async Task WithStoreAsync(Func<IWhatsAppMessageStore, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IWhatsAppMessageStore>());
    }
}
