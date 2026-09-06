using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.Domain.Leases;

public sealed class Lease
{
    private Lease()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid RoomId { get; private set; }
    public LeaseMode Mode { get; private set; }
    public decimal ContractedRate { get; private set; }
    public DateTimeOffset BillingStartAt { get; private set; }
    public int? BillingDueDay { get; private set; }
    public DateTimeOffset OccupancyStartAt { get; private set; }
    public DateTimeOffset? OccupancyEndAt { get; private set; }
    public LeaseLifecycleState LifecycleState { get; private set; }
    public int? MonthlyAnchorDay { get; private set; }
    public DateTimeOffset? MaterializedThroughAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Lease Create(
        Guid tenantId,
        Guid professionalId,
        Guid roomId,
        LeaseMode mode,
        decimal contractedRate,
        DateTimeOffset billingStartAt,
        int? billingDueDay,
        DateTimeOffset occupancyStartAt,
        DateTimeOffset? occupancyEndAt,
        int? monthlyAnchorDay,
        DateTimeOffset occurredAt)
    {
        ValidateResourceId(tenantId, nameof(tenantId));
        ValidateResourceId(professionalId, nameof(professionalId));
        ValidateResourceId(roomId, nameof(roomId));

        var lease = new Lease
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProfessionalId = professionalId,
            RoomId = roomId,
            LifecycleState = LeaseLifecycleState.Open,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt),
            UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt)
        };
        lease.SetContract(
            mode, contractedRate, billingStartAt, billingDueDay,
            occupancyStartAt, occupancyEndAt, monthlyAnchorDay);
        return lease;
    }

    public LeaseOperationalStatus GetOperationalStatus(DateTimeOffset now)
    {
        if (LifecycleState == LeaseLifecycleState.Cancelled) return LeaseOperationalStatus.Cancelled;
        if (LifecycleState == LeaseLifecycleState.Ended) return LeaseOperationalStatus.Ended;
        if (LifecycleState == LeaseLifecycleState.EndingPending) return LeaseOperationalStatus.EndingPending;

        var current = TimestampNormalizer.ToUtcMicroseconds(now);
        if (current < OccupancyStartAt) return LeaseOperationalStatus.Scheduled;
        if (OccupancyEndAt is { } end && current >= end) return LeaseOperationalStatus.EndingPending;
        return LeaseOperationalStatus.Active;
    }

    public void PostponeOccupancy(
        DateTimeOffset newOccupancyStartAt,
        int? monthlyAnchorDay,
        DateTimeOffset occurredAt)
    {
        EnsureOpen();
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        var newStart = TimestampNormalizer.ToUtcMicroseconds(newOccupancyStartAt);
        if (timestamp >= OccupancyStartAt)
            throw new InvalidOperationException("A ocupação já foi iniciada.");
        if (newStart <= OccupancyStartAt)
            throw new ArgumentException("A nova ocupação deve ser posterior à atual.", nameof(newOccupancyStartAt));
        if (OccupancyEndAt is { } end && newStart >= end)
            throw new ArgumentException("O início deve ser anterior ao término.", nameof(newOccupancyStartAt));

        ValidateMonthlyAnchor(Mode, monthlyAnchorDay);
        OccupancyStartAt = newStart;
        MonthlyAnchorDay = monthlyAnchorDay;
        MaterializedThroughAt = null;
        UpdatedAt = timestamp;
    }

    public void Cancel(DateTimeOffset occurredAt)
    {
        EnsureOpen();
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        if (timestamp >= OccupancyStartAt)
            throw new InvalidOperationException("Somente uma locação agendada pode ser cancelada.");
        LifecycleState = LeaseLifecycleState.Cancelled;
        UpdatedAt = timestamp;
    }

    public void MarkEndingPending(DateTimeOffset occurredAt)
    {
        EnsureOpen();
        LifecycleState = LeaseLifecycleState.EndingPending;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void MarkEnded(DateTimeOffset occurredAt)
    {
        if (LifecycleState is not (LeaseLifecycleState.Open or LeaseLifecycleState.EndingPending))
            throw new InvalidOperationException("A locação não pode ser encerrada neste estado.");
        LifecycleState = LeaseLifecycleState.Ended;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    internal void SetMaterializedThrough(DateTimeOffset? value)
    {
        MaterializedThroughAt = value is null ? null : TimestampNormalizer.ToUtcMicroseconds(value.Value);
    }

    private void SetContract(
        LeaseMode mode,
        decimal contractedRate,
        DateTimeOffset billingStartAt,
        int? billingDueDay,
        DateTimeOffset occupancyStartAt,
        DateTimeOffset? occupancyEndAt,
        int? monthlyAnchorDay)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!RoomRate.IsValid(contractedRate)) throw new ArgumentOutOfRangeException(nameof(contractedRate));
        if (billingDueDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(billingDueDay));

        var billingStart = TimestampNormalizer.ToUtcMicroseconds(billingStartAt);
        var occupancyStart = TimestampNormalizer.ToUtcMicroseconds(occupancyStartAt);
        DateTimeOffset? occupancyEnd = occupancyEndAt is null
            ? null
            : TimestampNormalizer.ToUtcMicroseconds(occupancyEndAt.Value);
        if (billingStart > occupancyStart)
            throw new ArgumentException("O início de cobrança não pode ser posterior à ocupação.", nameof(billingStartAt));
        if (occupancyEnd is { } end && end <= occupancyStart)
            throw new ArgumentException("O término deve ser posterior ao início.", nameof(occupancyEndAt));
        if (mode is LeaseMode.Daily or LeaseMode.Hourly && occupancyEnd is null)
            throw new ArgumentException("A modalidade exige término.", nameof(occupancyEndAt));
        ValidateMonthlyAnchor(mode, monthlyAnchorDay);

        Mode = mode;
        ContractedRate = contractedRate;
        BillingStartAt = billingStart;
        BillingDueDay = billingDueDay;
        OccupancyStartAt = occupancyStart;
        OccupancyEndAt = occupancyEnd;
        MonthlyAnchorDay = monthlyAnchorDay;
    }

    private static void ValidateMonthlyAnchor(LeaseMode mode, int? monthlyAnchorDay)
    {
        if (mode == LeaseMode.Monthly && monthlyAnchorDay is not (>= 1 and <= 31))
            throw new ArgumentException("A modalidade mensal exige dia âncora válido.", nameof(monthlyAnchorDay));
        if (mode != LeaseMode.Monthly && monthlyAnchorDay is not null)
            throw new ArgumentException("A modalidade não usa dia âncora.", nameof(monthlyAnchorDay));
    }

    private static void ValidateResourceId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("O recurso deve ter identidade válida.", parameterName);
    }

    private void EnsureOpen()
    {
        if (LifecycleState != LeaseLifecycleState.Open)
            throw new InvalidOperationException("A locação não está aberta.");
    }
}
