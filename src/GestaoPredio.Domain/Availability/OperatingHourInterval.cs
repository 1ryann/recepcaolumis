namespace GestaoPredio.Domain.Availability;

public readonly record struct LocalTimeRange(TimeOnly OpensAt, TimeOnly ClosesAt);

public sealed class OperatingHourInterval
{
    private OperatingHourInterval() { }

    public Guid Id { get; private set; }
    public Guid ScheduleId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly OpensAt { get; private set; }
    public TimeOnly ClosesAt { get; private set; }

    public static IReadOnlyList<OperatingHourInterval> CreateDay(
        Guid scheduleId,
        DayOfWeek dayOfWeek,
        IReadOnlyCollection<LocalTimeRange> ranges)
    {
        if (scheduleId == Guid.Empty) throw new ArgumentException("A configuração deve ser informada.", nameof(scheduleId));
        if (!Enum.IsDefined(dayOfWeek)) throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        ArgumentNullException.ThrowIfNull(ranges);
        var ordered = ranges.OrderBy(range => range.OpensAt).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].ClosesAt <= ordered[index].OpensAt)
                throw new ArgumentException("O fechamento deve ser posterior à abertura.", nameof(ranges));
            if (index > 0 && ordered[index].OpensAt < ordered[index - 1].ClosesAt)
                throw new ArgumentException("Os intervalos do dia não podem se sobrepor.", nameof(ranges));
        }
        return ordered.Select(range => new OperatingHourInterval
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            DayOfWeek = dayOfWeek,
            OpensAt = range.OpensAt,
            ClosesAt = range.ClosesAt
        }).ToArray();
    }
}
