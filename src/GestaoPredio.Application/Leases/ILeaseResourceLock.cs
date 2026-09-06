namespace GestaoPredio.Application.Leases;

public interface ILeaseResourceLock
{
    Task AcquireAsync(LeaseResourceLockRequest request, CancellationToken cancellationToken);
}

public sealed record LeaseResourceLockRequest(
    IReadOnlyCollection<Guid> TenantIds,
    IReadOnlyCollection<Guid> RoomIds,
    IReadOnlyCollection<Guid> ProfessionalIds);

public interface ILeaseConflictDetector
{
    Task<LeaseResourceConflict> FindConflictAsync(
        Guid roomId,
        Guid professionalId,
        DateTimeOffset occupancyStartAt,
        DateTimeOffset? occupancyEndAt,
        Guid? excludedLeaseId,
        CancellationToken cancellationToken);
}

public sealed record LeaseResourceConflict(bool Room, bool Professional)
{
    public bool Any => Room || Professional;
}
