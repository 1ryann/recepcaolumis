using System.Collections.Concurrent;
using GestaoPredio.Application.Whatsapp;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Stand-in for the Cloud API: records every template send and answers from a script (default: accepted with a
/// fresh wamid). Thread-safe, so two dispatchers can share it. It never touches the network.
/// </summary>
public sealed class FakeWhatsAppService : IWhatsAppService
{
    private readonly ConcurrentQueue<object> script = new();
    private int counter;

    public ConcurrentQueue<(string Phone, WhatsAppTemplate Template)> Sent { get; } = new();

    /// <summary>The biz_opaque_callback_data of each send, in order.</summary>
    public ConcurrentQueue<string?> CallbackData { get; } = new();

    /// <summary>Optional pause inside each send, to make two concurrent dispatchers overlap.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Runs inside each send after it was recorded and before it answers: the request is "at Meta".</summary>
    public Func<CancellationToken, Task>? OnSend { get; set; }

    public FakeWhatsAppService Then(params WhatsAppSendResult[] results)
    {
        foreach (var result in results) script.Enqueue(result);
        return this;
    }

    /// <summary>The next send throws after the request was recorded, like a client failing mid-call.</summary>
    public FakeWhatsAppService ThenThrow(Exception exception)
    {
        script.Enqueue(exception);
        return this;
    }

    public Task<WhatsAppSendResult> SendTextAsync(string destinationPhone, string body, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Operational notifications must use templates, never free text.");

    public async Task<WhatsAppSendResult> SendTemplateAsync(string destinationPhone, WhatsAppTemplate template,
        string? callbackData, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        Sent.Enqueue((destinationPhone, template));
        CallbackData.Enqueue(callbackData);
        if (OnSend is { } onSend) await onSend(cancellationToken);
        if (!script.TryDequeue(out var scripted))
            return WhatsAppSendResult.Succeeded($"wamid.fake.{Interlocked.Increment(ref counter)}");
        return scripted is Exception exception ? throw exception : (WhatsAppSendResult)scripted;
    }
}
