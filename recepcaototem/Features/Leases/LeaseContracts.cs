using recepcaototem.Features.Common;

namespace recepcaototem.Features.Leases;

public sealed record CreateLeaseRequest(
    Guid TenantId,
    Guid ProfessionalId,
    Guid RoomId,
    string? Mode,
    decimal ContractedRate,
    DateTimeOffset BillingStartAt,
    int? BillingDueDay,
    DateTimeOffset OccupancyStartAt,
    DateTimeOffset? OccupancyEndAt) : IStrictModuleRequest;

public sealed record UpdateLeaseRequest(
    Guid TenantId,
    Guid ProfessionalId,
    Guid RoomId,
    string? Mode,
    decimal ContractedRate,
    DateTimeOffset BillingStartAt,
    int? BillingDueDay,
    DateTimeOffset OccupancyStartAt,
    DateTimeOffset? OccupancyEndAt,
    string? ConcurrencyToken) : IStrictModuleRequest;

public sealed record PostponeLeaseRequest(DateTimeOffset OccupancyStartAt, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record LeaseConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record EndLeaseRequest(DateTimeOffset? EndAt, string? ConcurrencyToken) : IStrictModuleRequest;

public sealed record LeaseResponse(
    Guid Id,
    Guid TenantId,
    string TenantName,
    Guid ProfessionalId,
    string ProfessionalName,
    Guid RoomId,
    string RoomName,
    string Mode,
    decimal ContractedRate,
    DateTimeOffset BillingStartAt,
    int? BillingDueDay,
    DateTimeOffset OccupancyStartAt,
    DateTimeOffset? OccupancyEndAt,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ConcurrencyToken);
