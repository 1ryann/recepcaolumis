namespace GestaoPredio.Application.Leases;

public interface ILeaseOpenVisitProbe
{
    Task<bool> HasOpenVisitsAsync(Guid leaseId, CancellationToken cancellationToken);
}

public sealed class NoOpenVisitProbe : ILeaseOpenVisitProbe
{
    public Task<bool> HasOpenVisitsAsync(Guid leaseId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
