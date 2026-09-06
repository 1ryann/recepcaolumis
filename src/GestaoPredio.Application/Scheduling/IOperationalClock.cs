namespace GestaoPredio.Application.Scheduling;

public interface IOperationalClock
{
    DateTimeOffset UtcNow { get; }
    TimeZoneInfo TimeZone { get; }
}

public sealed class OperationalClock(TimeProvider timeProvider, TimeZoneInfo timeZone) : IOperationalClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    public TimeZoneInfo TimeZone { get; } = timeZone;
}
