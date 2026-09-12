using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record ProfessionalProfileResponse(
    string Name,
    string Profession,
    string? Description,
    string WhatsApp,
    bool HasPhoto,
    string? PhotoUrl,
    string ConcurrencyToken);

public sealed record ProfessionalProfileUpdateRequest(
    string WhatsApp,
    string? Description,
    string ConcurrencyToken) : IStrictModuleRequest;
