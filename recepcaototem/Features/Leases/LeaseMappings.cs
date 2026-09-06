using GestaoPredio.Domain.Leases;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Leases;

internal static class LeaseMappings
{
    public static string ToContract(this LeaseMode mode) => mode switch
    {
        LeaseMode.Monthly => "MONTHLY",
        LeaseMode.Daily => "DAILY",
        LeaseMode.Hourly => "HOURLY",
        _ => throw new InvalidOperationException("Modalidade de locação desconhecida.")
    };

    public static string ToContract(this LeaseOperationalStatus status) => status switch
    {
        LeaseOperationalStatus.Scheduled => "AGENDADA",
        LeaseOperationalStatus.Active => "ATIVA",
        LeaseOperationalStatus.EndingPending => "ENCERRAMENTO_PENDENTE",
        LeaseOperationalStatus.Ended => "ENCERRADA",
        LeaseOperationalStatus.Cancelled => "CANCELADA",
        _ => throw new InvalidOperationException("Status de locação desconhecido.")
    };

    public static LeaseResponse ToResponse(
        this Lease lease,
        string tenantName,
        string professionalName,
        string roomName,
        DateTimeOffset now) => new(
            lease.Id,
            lease.TenantId,
            tenantName,
            lease.ProfessionalId,
            professionalName,
            lease.RoomId,
            roomName,
            lease.Mode.ToContract(),
            lease.ContractedRate,
            lease.BillingStartAt,
            lease.BillingDueDay,
            lease.OccupancyStartAt,
            lease.OccupancyEndAt,
            lease.GetOperationalStatus(now).ToContract(),
            lease.CreatedAt,
            lease.UpdatedAt,
            ConcurrencyToken.Encode(lease.Version));
}
