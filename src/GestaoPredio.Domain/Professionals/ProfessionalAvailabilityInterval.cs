namespace GestaoPredio.Domain.Professionals;

public readonly record struct ProfessionalLocalTimeRange(TimeOnly StartTime, TimeOnly EndTime);

public sealed class ProfessionalAvailabilityInterval
{
    private ProfessionalAvailabilityInterval() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public static IReadOnlyList<ProfessionalAvailabilityInterval> CreateDay(
        Guid professionalId,
        DayOfWeek dayOfWeek,
        IReadOnlyCollection<ProfessionalLocalTimeRange> ranges)
    {
        if (professionalId == Guid.Empty) throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        if (!Enum.IsDefined(dayOfWeek)) throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        ArgumentNullException.ThrowIfNull(ranges);

        var ordered = ranges.OrderBy(value => value.StartTime).ThenBy(value => value.EndTime).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].EndTime <= ordered[index].StartTime)
                throw new ArgumentException("O fim deve ser posterior ao início.", nameof(ranges));
            if (index > 0 && ordered[index].StartTime < ordered[index - 1].EndTime)
                throw new ArgumentException("Os intervalos do dia não podem se sobrepor.", nameof(ranges));
        }

        return ordered.Select(value => new ProfessionalAvailabilityInterval
        {
            Id = Guid.NewGuid(),
            ProfessionalId = professionalId,
            DayOfWeek = dayOfWeek,
            StartTime = value.StartTime,
            EndTime = value.EndTime
        }).ToArray();
    }
}
