namespace GestaoPredio.Application.Leases;

public interface ILeaseResourceLock
{
    Task AcquireAsync(LeaseResourceLockRequest request, CancellationToken cancellationToken);
}

public sealed record LeaseResourceLockRequest(
    IReadOnlyCollection<Guid> TenantIds,
    IReadOnlyCollection<Guid> RoomIds,
    IReadOnlyCollection<Guid> ProfessionalIds);
