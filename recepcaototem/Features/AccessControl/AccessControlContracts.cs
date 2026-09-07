using recepcaototem.Features.Common;

namespace recepcaototem.Features.AccessControl;

public sealed record ReleaseDoorRequest(Guid? VisitId) : IStrictModuleRequest;

public sealed record ReleaseDoorResponse(
    bool Success,
    bool CommandAccepted,
    string Provider,
    string? FailureCode);
