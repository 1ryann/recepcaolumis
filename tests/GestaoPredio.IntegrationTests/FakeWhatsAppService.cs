using System.Collections.Concurrent;
using GestaoPredio.Application.Whatsapp;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Stand-in for the Cloud API: records every template send and answers from a script (default: accepted with a
/// fresh wamid). Thread-safe, so two dispatchers can share it. It never touches the network.
/// </summary>
public sealed class FakeWhatsAppService : IWhatsAppService
{
    private readonly ConcurrentQueue<WhatsAppSendResult> script = new();
    private int counter;

    public ConcurrentQueue<(string Phone, WhatsAppTemplate Template)> Sent { get; } = new();

    /// <summary>Optional pause inside each send, to make two concurrent dispatchers overlap.</summary>
    public TimeSpan Delay { get; init; }

    public FakeWhatsAppService Then(params WhatsAppSendResult[] results)
    {
        foreach (var result in results) script.Enqueue(result);
        return this;
    }

    public Task<WhatsAppSendResult> SendTextAsync(string destinationPhone, string body, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Operational notifications must use templates, never free text.");

    public async Task<WhatsAppSendResult> SendTemplateAsync(string destinationPhone, WhatsAppTemplate template,
        CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        Sent.Enqueue((destinationPhone, template));
        return script.TryDequeue(out var scripted)
            ? scripted
            : WhatsAppSendResult.Succeeded($"wamid.fake.{Interlocked.Increment(ref counter)}");
    }
}
