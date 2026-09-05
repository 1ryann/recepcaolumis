using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record ProfessionalUserLinkRequest(string? ApplicationUserId, string? ConcurrencyToken)
    : IStrictModuleRequest;

public sealed record ProfessionalUserUnlinkRequest(string? ConcurrencyToken) : IStrictModuleRequest;

public sealed record EligibleUserResponse(string UserId, string DisplayName, string Email);

public sealed record ProfessionalUserLinkResponse(
    bool Linked,
    string? UserId = null,
    string? DisplayName = null,
    string? Email = null);
