using recepcaototem.Api.Configuration;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Availability;

public sealed record ProfessionalAvailabilityIntervalRequest(string? StartTime, string? EndTime) : IStrictModuleRequest;
public sealed record ProfessionalAvailabilityDayRequest(string? DayOfWeek,
    IReadOnlyList<ProfessionalAvailabilityIntervalRequest>? Intervals) : IStrictModuleRequest;
public sealed record UpdateProfessionalAvailabilityRequest(string? Mode,
    IReadOnlyList<ProfessionalAvailabilityDayRequest>? Days, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record ProfessionalAvailabilityIntervalResponse(string StartTime, string EndTime);
public sealed record ProfessionalAvailabilityDayResponse(string DayOfWeek,
    ProfessionalAvailabilityIntervalResponse[] Intervals);
public sealed record ProfessionalAvailabilityResponse(Guid ProfessionalId, string Mode,
    ProfessionalAvailabilityDayResponse[] Days, ProfessionalAvailabilityDayResponse[] EffectiveDays,
    string ConcurrencyToken, int ExistingReservationsOutsideAvailabilityCount);

public sealed record CreateProfessionalAvailabilityExceptionRequest(DateOnly Date, bool AllDay,
    string? StartTime, string? EndTime, string? Reason) : IStrictModuleRequest;
public sealed record UpdateProfessionalAvailabilityExceptionRequest(DateOnly Date, bool AllDay,
    string? StartTime, string? EndTime, string? Reason, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record DeleteProfessionalAvailabilityExceptionRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record ProfessionalAvailabilityExceptionResponse(Guid Id, Guid ProfessionalId,
    DateOnly Date, bool AllDay, string? StartTime, string? EndTime, string? Reason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string ConcurrencyToken,
    int ExistingReservationsOutsideAvailabilityCount);
