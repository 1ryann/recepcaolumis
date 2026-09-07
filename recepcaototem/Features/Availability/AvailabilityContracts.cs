using recepcaototem.Features.Common;

namespace recepcaototem.Features.Availability;

public sealed record OperatingHourIntervalRequest(string? OpensAt, string? ClosesAt) : IStrictModuleRequest;
public sealed record OperatingHoursDayRequest(string? DayOfWeek,
    IReadOnlyList<OperatingHourIntervalRequest>? Intervals) : IStrictModuleRequest;
public sealed record UpdateOperatingHoursRequest(IReadOnlyList<OperatingHoursDayRequest>? Days,
    string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record OperatingHourIntervalResponse(string OpensAt, string ClosesAt);
public sealed record OperatingHoursDayResponse(string DayOfWeek,
    IReadOnlyList<OperatingHourIntervalResponse> Intervals);
public sealed record OperatingHoursResponse(bool Configured,
    IReadOnlyList<OperatingHoursDayResponse> Days, string? ConcurrencyToken);

public sealed record CreateRoomBlockRequest(Guid RoomId, DateTimeOffset StartAt,
    DateTimeOffset EndAt, string? Reason) : IStrictModuleRequest;
public sealed record UpdateRoomBlockRequest(DateTimeOffset StartAt, DateTimeOffset EndAt,
    string? Reason, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record RoomBlockConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record RoomBlockResponse(Guid Id, Guid RoomId, string RoomName,
    DateTimeOffset StartAt, DateTimeOffset EndAt, string Reason, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CancelledAt,
    string ConcurrencyToken);
