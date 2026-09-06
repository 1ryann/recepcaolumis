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

