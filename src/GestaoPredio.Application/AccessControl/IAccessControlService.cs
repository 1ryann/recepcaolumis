namespace GestaoPredio.Application.AccessControl;

public sealed record AccessControlReleaseRequest(
    string DoorId,
    Guid? VisitId,
    string ActorUserId);

public sealed record AccessControlResult(
    bool Success,
    bool CommandAccepted,
    string Provider,
    string? FailureCode)
{
    public static AccessControlResult Accepted(string provider) => new(true, true, provider, null);
    public static AccessControlResult Failed(string provider, string failureCode) => new(false, false, provider, failureCode);
}

public interface IAccessControlService
{
    Task<AccessControlResult> ReleaseDoorAsync(
        AccessControlReleaseRequest request,
        CancellationToken cancellationToken);
}

public interface IAccessControlProvider
{
    string Name { get; }

    Task<AccessControlResult> ReleaseDoorAsync(
        AccessControlReleaseRequest request,
        CancellationToken cancellationToken);
}
