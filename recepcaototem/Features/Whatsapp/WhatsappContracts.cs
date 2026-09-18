using recepcaototem.Features.Common;

namespace recepcaototem.Features.Whatsapp;

/// <summary>Admin-only manual send. Strict body: no token or sender override can be supplied by the client.</summary>
public sealed record WhatsappTestMessageRequest(string? PhoneNumber, string? Message) : IStrictModuleRequest;

public sealed record WhatsappTestMessageResponse(bool Success, string? MessageId, string? FailureCode);
