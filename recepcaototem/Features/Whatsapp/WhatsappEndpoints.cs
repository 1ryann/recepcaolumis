using System.Security.Claims;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Whatsapp;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Whatsapp;

public static class WhatsappEndpoints
{
    public const string TestMessagePath = "/api/admin/whatsapp/test";
    public const string WebhookPath = "/api/whatsapp/webhook";
    private const int MaxTestMessageLength = 1000;
    private const int MaxWebhookBodyBytes = 1024 * 1024;
    private const string AuditTargetType = "WHATSAPP_MESSAGE";

    public static IEndpointRouteBuilder MapWhatsappEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(TestMessagePath, SendTestMessage)
            .RequireAuthorization("Administration")
            .AddEndpointFilter<AntiforgeryFilter>();
        // Called by Meta, not by a browser session: authenticity comes from the verify token (GET) and the
        // X-Hub-Signature-256 HMAC (POST), so cookie auth and CSRF do not apply.
        endpoints.MapGet(WebhookPath, VerifyWebhook).AllowAnonymous();
        endpoints.MapPost(WebhookPath, ReceiveWebhook).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> SendTestMessage(
        WhatsappTestMessageRequest request,
        HttpContext context,
        ApplicationDbContext db,
        IWhatsAppService whatsapp,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim() ?? "";
        if (!WhatsAppNormalizer.TryNormalize(request.PhoneNumber, out var phone))
            return Results.BadRequest(new ApiError("WHATSAPP_RECIPIENT_INVALID", "Informe um número de WhatsApp válido."));
        if (message.Length is 0 or > MaxTestMessageLength)
            return Results.BadRequest(new ApiError("WHATSAPP_MESSAGE_INVALID", $"A mensagem deve ter entre 1 e {MaxTestMessageLength} caracteres."));

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await WriteAuditAsync(db, context, actor, "WHATSAPP_TEST_REQUESTED", "SUCCEEDED", null, time.GetUtcNow(), cancellationToken);

        var result = await whatsapp.SendTextAsync(phone, message, cancellationToken);
        await WriteAuditAsync(db, context, actor, result.Success ? "WHATSAPP_TEST_SUCCEEDED" : "WHATSAPP_TEST_FAILED",
            result.Success ? "SUCCEEDED" : "FAILED", result.FailureCode, time.GetUtcNow(), cancellationToken);

        var response = new WhatsappTestMessageResponse(result.Success, result.MessageId, result.FailureCode);
        return Results.Json(response, statusCode: StatusFor(result));
    }

    private static int StatusFor(WhatsAppSendResult result) => result.FailureCode switch
    {
        null => StatusCodes.Status200OK,
        WhatsAppFailureCodes.NotConfigured => StatusCodes.Status503ServiceUnavailable,
        WhatsAppFailureCodes.InvalidRecipient or WhatsAppFailureCodes.InvalidMessage => StatusCodes.Status400BadRequest,
        WhatsAppFailureCodes.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status502BadGateway
    };

    private static IResult VerifyWebhook(HttpContext context, IOptionsMonitor<WhatsAppCloudOptions> options)
    {
        var query = context.Request.Query;
        var challenge = query["hub.challenge"].ToString();
        if (query["hub.mode"] != "subscribe" || string.IsNullOrEmpty(challenge) || challenge.Length > 256 ||
            !WhatsAppWebhookSecurity.IsVerifyTokenValid(query["hub.verify_token"], options.CurrentValue.VerifyToken))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        return Results.Text(challenge, "text/plain");
    }

    private static async Task<IResult> ReceiveWebhook(
        HttpContext context,
        IOptionsMonitor<WhatsAppCloudOptions> options,
        WhatsAppStatusRecorder statuses,
        IWhatsAppMessageStore messages,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxWebhookBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxWebhookBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }

        var body = buffer.ToArray();
        if (!WhatsAppWebhookSecurity.IsSignatureValid(body, context.Request.Headers[WhatsAppWebhookSecurity.SignatureHeader],
                options.CurrentValue.AppSecret))
            return Results.Unauthorized();

        var payload = WhatsAppWebhookParser.Parse(body);
        if (!payload.Parsed) return Results.BadRequest();

        var logger = loggerFactory.CreateLogger("recepcaototem.Features.Whatsapp.Webhook");
        // Counts only: no sender, contact name or message text reaches the logs. Inbound messages are counted
        // but not processed — conversational handling is out of scope for this base infrastructure.
        logger.LogInformation(
            "WhatsApp webhook event received. Object: {Object}; Entries: {Entries}; Messages: {Messages}; Statuses: {Statuses}",
            payload.Object is { Length: <= 64 } ? payload.Object : "unknown", payload.EntryCount,
            payload.InboundMessageCount, payload.Statuses.Count);

        foreach (var status in payload.Statuses)
        {
            // The database decides what is new: the unique wamid plus the forward-only transition rules make
            // replays, out-of-order deliveries and a second instance harmless. The in-memory recorder is only
            // telemetry. The recipient is never logged unmasked.
            var applied = await messages.ApplyStatusAsync(status.ToUpdate(), cancellationToken);
            statuses.TryRecord(status);
            if (!applied) continue;
            logger.LogInformation(
                "WhatsApp message status. MessageId: {MessageId}; Status: {Status}; Recipient: {Recipient}; OccurredAt: {OccurredAt}; PhoneNumberId: {PhoneNumberId}; WabaId: {WabaId}; ErrorCode: {ErrorCode}; ErrorTitle: {ErrorTitle}",
                status.MessageId, status.RawStatus, status.MaskedRecipient, status.OccurredAt, status.PhoneNumberId,
                status.WhatsAppBusinessAccountId, status.ErrorCode, status.ErrorTitle);
        }

        return Results.Ok();
    }

    private static async Task WriteAuditAsync(ApplicationDbContext db, HttpContext context, string? actor, string action,
        string result, string? failureCode, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        // Recipient and message text are deliberately not audited; only the outcome and stable failure code.
        var suffix = string.IsNullOrWhiteSpace(failureCode) ? "" : $":{failureCode}";
        var correlation = $"{context.TraceIdentifier}{suffix}";
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actor,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            Action = action,
            Result = result,
            OccurredAt = occurredAt,
            CorrelationId = correlation.Length > 100 ? correlation[..100] : correlation,
            TargetEntityType = AuditTargetType
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
